# ============================================================
# DATN_BAN_THU_GON_SAFE
# Base logic: original dual-camera YOLO/hand pipeline
# UI compact: only 2 camera views + PALM SENSOR TEST button
# Both MAIN and PHONE cameras detect continuously; no camera selection hotkeys
# ============================================================
# BAN DA TICH HOP: YOLO 3 OBJECTS AUTO GRIP + BOTH CAM HAND DETECT
# Khong can file module phu. Chay truc tiep file nay.
# ============================================================

import cv2
import numpy as np
import mediapipe as mp
from mediapipe.framework.formats import landmark_pb2
import socket
import json
import math
import time
from collections import deque, Counter
from dataclasses import dataclass
from pathlib import Path
try:
    import datn_web_dashboard_v7_real_camera as _datn_web_module
    from datn_web_dashboard_v7_real_camera import (
        start_web_dashboard,
        update_dashboard,
        update_camera_frame,
        get_web_control_snapshot,
    )
    WEB_DASHBOARD_IMPORT_OK = True
    WEB_DASHBOARD_IMPORT_ERROR = ""
except Exception as _web_e:
    _datn_web_module = None
    start_web_dashboard = None
    update_dashboard = None
    update_camera_frame = None
    get_web_control_snapshot = None
    WEB_DASHBOARD_IMPORT_OK = False
    WEB_DASHBOARD_IMPORT_ERROR = str(_web_e)
try:
    import serial
except Exception:
    serial = None

# ===================== NETWORK / UNITY =====================
UNITY_IP = "127.0.0.1"
UNITY_PORT = 5052

# Unity object mover link:
# - Port 5052: JSON hand + force data for HandUdpReceiver / Force UI.
# - Port 5053: plain text object name for PythonObjectUdpLink.cs.
#   Unity will move 3D object automatically when it receives:
#   kimquat_sauce / red_chili / orange / none.
UNITY_OBJECT_PORT = 5053
UNITY_OBJECT_SEND_INTERVAL = 0.10

# ===================== CAMERA SETUP =====================
# MAIN: laptop/USB camera
CAM_MAIN_SOURCE = 1

# PHONE: iPhone through Iriun Webcam. You said Iriun is camera index 0.
CAM_PHONE_ENABLE = True
CAM_PHONE_SOURCE = 0
CAM_PHONE_NAME = "IPHONE_IRIUN"

# Both cameras always detect. No manual camera selection in compact UI.
# Internal control packet is auto-selected, but both camera views are displayed.
CAM_SELECT_MODE = "MAIN"   # V59: Laptop/Main cam lam CONTROL; Phone cam chi detect/hien thi/du phong goc nhin
CAM_AUTO_PREFER = "MAIN"

# UI / camera size
FRAME_W = 640
FRAME_H = 480
# BAN THU GON: cua so Python chi hien 2 camera + nut PALM SENSOR
# Kich thuoc gon de keo vao vung trong ben phai cua web dashboard.
WINDOW_W = 800
WINDOW_H = 340

# Scale tong the cua so Python de uu tien theo doi Unity
# Chi thu nho man hinh hien thi, khong doi bo cuc dashboard ben trong
PY_WINDOW_SCALE = 1.0
SHOW_WINDOW_W = int(WINDOW_W * PY_WINDOW_SCALE)
SHOW_WINDOW_H = int(WINDOW_H * PY_WINDOW_SCALE)
WIN_NAME = "DATN_V59_MAIN_CONTROL_PHONE_VIEW"

# ===================== WEB DASHBOARD LAN V7 =====================
# Chay code Python chinh la web tu bat.
# Web chi nhan thong so that tu Python.
# Camera chi hien tren cua so Python thu gon, khong day camera len web.
WEB_DASHBOARD_ENABLE = True
WEB_DASHBOARD_PORT = 5000
WEB_DASHBOARD_OPEN_BROWSER = True
WEB_DASHBOARD_DATA_INTERVAL = 0.08  # V30: cap nhat web nhanh hon de thanh bot lag
WEB_DASHBOARD_CAM_INTERVAL = 9999.0  # V30: khong day camera len web, camera hien trong cua so Python
web_dashboard_ok = False
last_web_update = 0.0
last_web_cam_update = 0.0
WEB_CONTROL_FROM_DASHBOARD_ENABLE = True
last_web_control_apply_time = 0.0
last_web_palm_toggle_seq = 0
last_web_reset_to_tracking_seq = 0
WEB_RESET_OBJECT_IGNORE_SEC = 1.20
web_reset_ignore_object_until = 0.0

# V30: Camera da dua len web. Ban thu gon mac dinh khong hien cua so OpenCV de khong che web/Unity.
PYTHON_PREVIEW_ENABLE = True

# ===================== AUTO / MANU CONTROL =====================
# AUTO: chay nhu code hien tai, camera thay ban tay la dieu khien.
# MANU: bam/tick truc tiep tren dashboard roi keo 5 thanh trong man hinh dieu khien.
CONTROL_MODE = "AUTO"
MANUAL_FINGER_ORDER = ["THUMB", "INDEX", "MIDDLE", "RING", "PINKY"]
MANUAL_MAX_PCT_PER_SEC = 45.0   # Toc do tang/giam % khi keo slider, nho hon = mem hon

# Hitbox chuot tren dashboard. Toa do duoc tinh theo display goc 1280x920,
# nhung mouse callback se tu quy doi theo PY_WINDOW_SCALE.
ui_hitboxes = []
ui_drag_finger = None

CAM_VIEW_W = WINDOW_W // 2
TOP_VIEW_H = 360
INFO_H = WINDOW_H - TOP_VIEW_H
CAP_BUFFER_SIZE = 1

# If True: both cams run MediaPipe every frame. This prevents flicker in preview.
# If your PC lags, set this False; only active cam runs every frame.
PROCESS_BOTH_CAMS = True
SECONDARY_PROCESS_EVERY_N = 4
PHONE_READ_EVERY_N = 1
DETECTION_HOLD_SEC = 0.25

# ===================== DUAL-CAMERA STABLE CONTROL =====================
# Both cameras still run hand detection continuously.  Only the control
# ownership is kept stable, so a one-frame miss cannot make the robot jump
# between two different viewpoints.
DUAL_CAM_SWITCH_CONFIRM_FRAMES = 8
DUAL_CAM_PACKET_HOLD_SEC = 1.20
DUAL_CAM_BLEND_SEC = 0.60

# Each camera owns a completely independent temporal filter.  Never share a
# median/EMA state between cameras because their viewpoints and angle biases
# are different.
CAM_FILTER_MEDIAN_WIN = 5
CAM_FILTER_LOST_RESET_FRAMES = 10

# Explicit open-state lock.  A finger must remain clearly bent for several
# frames before leaving 0%, and clearly open for several frames before being
# snapped back to 0%.  Thumb needs a wider threshold because MediaPipe thumb
# depth is noisier than the other four fingers.
CAM_OPEN_UNLOCK_PCT = {
    "thumb": 36.0,
    "index": 27.0,
    "middle": 27.0,
    "ring": 28.0,
    "pinky": 30.0,
}
CAM_OPEN_RELOCK_PCT = {
    "thumb": 20.0,
    "index": 14.0,
    "middle": 14.0,
    "ring": 15.0,
    "pinky": 17.0,
}
CAM_OPEN_UNLOCK_FRAMES = 2
CAM_OPEN_RELOCK_FRAMES = 3

# Geometry guard before percentage calibration.  These values intentionally
# tolerate a non-perfect 180-degree MediaPipe pose while the real finger is
# visually straight.
THUMB_GEOM_OPEN_MCP_DEG = 150.0
THUMB_GEOM_OPEN_IP_DEG = 154.0
FINGER_GEOM_OPEN_MCP_DEG = 145.0
FINGER_GEOM_OPEN_PIP_DEG = 154.0
FINGER_GEOM_OPEN_DIP_DEG = 152.0

# ===================== SERIAL / ESP32 =====================
# The file name is PYTHON, but serial is kept because the original code sends servo/FSR to ESP32.
SERIAL_ENABLE = True
SERIAL_PORT = "COM3"
SERIAL_BAUD = 115200
SERIAL_MODE = "ALL_5"
SERIAL_SEND_INTERVAL = 0.010

# V10 MATCH ESP32 LAN33 MICROSECOND:
# ESP32 LAN33 da map _PCT theo dung nghia:
#   0%   = openUs
#   100% = closeUs
# Huong nguoc/thuan cua tung servo da nam trong openUs/closeUs ben ESP32.
# Vi vay Python KHONG duoc invert nua.
SERVO_INVERT = {
    "PINKY": False,
    "RING": False,
    "MIDDLE": False,
    "INDEX": False,
    "THUMB": False,
}

# Mechanical compensation for real robot
SERVO_MECH_COMP = {
    # FIX V56: Thumb MediaPipe often reports only ~40-55% even when the real thumb
    # is visually folded into the palm. Boost thumb command after it starts closing.
    "THUMB": {"start": 8.0, "gain": 1.55, "offset": 18.0},
    "PINKY": {"start": 10.0, "gain": 1.22, "offset": 8.0},
    "INDEX": None,
    "MIDDLE": None,
    "RING": None,
}

# ===================== MEDIAPIPE / FILTERS =====================
MIN_DET_CONF = 0.72
MIN_TRK_CONF = 0.75
MP_MODEL_COMPLEXITY = 1

# Original angle-output filtering from code dau
EMA_ALPHA_SLOW = 0.18
EMA_ALPHA_FAST = 0.58
MOVE_THRESH = 4.0
FAST_THRESH = 16.0

ZERO_ENTER = 9.0
ZERO_EXIT = 4.0

FINGER_ENTER = 13.0
FINGER_EXIT = 6.0

THUMB_HARD_ZERO_2 = 15.0
THUMB_HARD_ZERO_3 = 15.0
THUMB_SOFT_ZERO_2 = 26.0
THUMB_SOFT_ZERO_3 = 24.0

FINGER_HARD_ZERO_2 = 18.0
FINGER_HARD_ZERO_3 = 18.0
FINGER_SOFT_ZERO_2 = 30.0
FINGER_SOFT_ZERO_3 = 26.0

FINAL_ZERO_SNAP = 0.8
MAX_RATE_PER_SEC = 380.0
LOST_HOLD_SEC = 0.10

# Extra anti-jitter only smooths landmark positions; it does not change the original angle formula.
LANDMARK_SMOOTH_ENABLE = True
LANDMARK_ALPHA_SLOW = 0.18
LANDMARK_ALPHA_FAST = 0.62
LANDMARK_DIST_SLOW = 0.004
LANDMARK_DIST_FAST = 0.040
LANDMARK_RESET_LOST_FRAMES = 8

# Unity send branch
MIN_SEND_INTERVAL = 0.01
SEND_EPS = 0.25

UNITY_CURVE_POWER = 1.08
UNITY_THUMB_CURVE_POWER = 1.10
UNITY_MASTER_SCALE = 0.98
UNITY_FINGER_ENTER = 16.0
UNITY_FINGER_EXIT = 8.0
UNITY_JOINT_ZERO = 1.5
UNITY_GRIP_START = 56.0

UNITY_SCALES = {
    "THUMB": (0.84, 0.82, 0.88),
    "INDEX": (0.92, 0.98, 0.94),
    "MIDDLE": (0.96, 1.02, 0.98),
    "RING": (0.96, 1.04, 1.00),
    "PINKY": (0.92, 0.98, 0.94),
}

UNITY_CAPS = {
    "THUMB": (84.0, 82.0, 88.0),
    "INDEX": (90.0, 96.0, 92.0),
    "MIDDLE": (94.0, 100.0, 96.0),
    "RING": (94.0, 100.0, 98.0),
    "PINKY": (90.0, 96.0, 92.0),
}

# Serial smoothing for real robot
SERIAL_MEDIAN_WIN = 3
SERIAL_EMA_ALPHA = 0.55
SERIAL_DEADBAND = 1
SERIAL_SEND_DELTA = 1
SERIAL_QUANT_STEP = 1
SERIAL_SNAP_LOW = 2
SERIAL_SNAP_HIGH = 98

# FSR feedback from ESP32
# V55:
#   THUMB FSR  -> cam bien luc rieng cua ngon cai.
#   PALM_KEY   -> nut/cam bien long ban tay doc rieng, chi dung de mo khoa nắm vat.
#   5 ngon THUMB/INDEX/MIDDLE/RING/PINKY deu tham gia tinh tong luc kep.
FSR_NAMES = ["PINKY", "RING", "MIDDLE", "INDEX", "THUMB"]
PALM_FSR_NAME = "THUMB"  # giu lai de tuong thich code cu; KHONG dung lam palm neu USE_THUMB_FSR_AS_PALM_SENSOR=False
FORCE_CONTROL_FSR_NAMES = ["THUMB", "INDEX", "MIDDLE", "RING", "PINKY"]
FINGER_FORCE_FSR_NAMES = FORCE_CONTROL_FSR_NAMES
FSR_UI_ORDER = ["THUMB", "INDEX", "MIDDLE", "RING", "PINKY"]  # THUMB card hien thi luc that cua ngon cai
FSR_FEEDBACK_ENABLE = True
FSR_STREAM_ON_AT_START = True
FSR_FORCE_ON_AT_START = True
FSR_STALE_SEC = 1.2
FSR_HIT_HOLD_SEC = 0.50

# Optional Python-side soft guard using FSR % feedback.
FSR_SOFT_GUARD_ENABLE = False
FSR_SOFT_GUARD_GAIN = 1.15
FSR_SOFT_GUARD_THRESH = {
    "THUMB": 82.0,
    "INDEX": 78.0,
    "MIDDLE": 78.0,
    "RING": 74.0,
    "PINKY": 74.0,
}

# ===================== LOADCELL -> DYNAMIC GRIP =====================
# ESP32 gui len: LOADCELL KG=... N=... RAW=...
# Y tuong: can vat bang loadcell -> Python tinh muc luc bam can thiet
# -> gui FORCE_THR moi cho ESP32 de FSR stop dung luc hon.
LOADCELL_ENABLE = True

# Tam thoi tat stream loadcell that vi HX711/loadcell dang nhieu.
# Python se uu tien khoi luong nhap tay tren dashboard.
LOADCELL_STREAM_ON_AT_START = False

LOADCELL_STALE_SEC = 1.8
LOADCELL_MIN_VALID_KG = 0.03
LOADCELL_MAX_KG = 5.0

# ===================== MANUAL LOADCELL INPUT =====================
# Click vao o nhap tren dashboard, go so gram mong muon, nhan Enter.
# Vi du nhap 180 -> Python coi vat nang 180g, tu do tinh target grip force.
MANUAL_LOADCELL_ENABLE = True
manual_loadcell_override = True
manual_loadcell_g = 0.0
manual_load_input_active = False
manual_load_input_text = ""

# Cong thuc:
# weight_N = mass_kg * 9.81
# target_grip_total_N = weight_N * safety_factor / friction_coeff
# Sau do chia cho so cam bien FSR dang tham gia kep.
GRIP_FRICTION_COEFF = 0.55
GRIP_SAFETY_FACTOR = 2.0
GRIP_SENSOR_SHARE_COUNT = 5.0  # chi chia cho 5 FSR ngon: INDEX/MIDDLE/RING/PINKY; THUMB FSR la PALM

# Khi camera dang nam va FSR bat dau cham vat thi moi kich hoat giu luc theo N.
FSR_CONTACT_N = 0.25
FSR_CONTACT_PCT = 5.0
GRIP_CLOSE_LEVEL_ENTER = 22.0
GRIP_CLOSE_LEVEL_EXIT = 12.0


# V41 SAFETY FIX:
# Khong cho vao FORCE_HOLD / full-close chi vi palm sensor da unlock + FSR ngon bi cham/nhiễu,
# khi camera chua thay ban tay dang nam.
# Dieu kien vao FORCE_HOLD moi:
#   co vat + palm unlock + co it nhat 1 FSR ngon cham + camera dang thay tay + tay dang dong ro.
FORCE_HOLD_REQUIRE_HAND_CLOSE = True
FORCE_HOLD_ENTRY_MIN_CLOSE_LEVEL = GRIP_CLOSE_LEVEL_ENTER

# V43: Sau khi Palm Sensor unlock, khong cho vao FORCE_HOLD ngay lap tuc.
# Muc dich: giu dung logic 'co vat -> mo 0%, cham palm -> moi follow tay',
# va cho robot/web co thoi gian dong muot thay vi nhay thang sang full-close.
FORCE_HOLD_ARM_DELAY_SEC = 0.75

# V45: Khi da cham Palm Sensor va camera con thay tay, uu tien demo follow tay.
# Tam thoi day nguong FORCE_THR_N len cao de ESP32 khong latch force-hit,
# vi force-hit co the chan servo mo ra sau khi FSR ngon da cham.
DIRECT_FOLLOW_AFTER_PALM_ENABLE = True
DIRECT_FOLLOW_DISABLE_FORCE_LIMIT_N = 30.0
DIRECT_FOLLOW_FORCE_DISABLE_INTERVAL_SEC = 0.25

# V44 DIRECT FOLLOW FIX:
# Sau khi YOLO da khoa vat va Palm Sensor da unlock, robot phai quay lai follow tay ngay,
# khong doi state machine chuyen sang VISION_GRIP_CLOSING_AUTO/WAIT_HUMAN_GRIP.
# Muc tieu: giai quyet hien tuong nam/mo bi tre vai giay sau khi da co vat.
DIRECT_FOLLOW_AFTER_PALM_ENABLE = True

# V30: Dang FORCE_HOLD van cho mo rieng tung ngon neu camera thay ngon do mo ro rang.
# Muc dich: demo duoc mo 1 ngon ma cac ngon khac van giu vat.
FORCE_HOLD_PER_FINGER_RELEASE_ENABLE = False
FINGER_RELEASE_OPEN_PCT = 18.0     # nho hon muc nay -> coi la ngon do dang mo
FINGER_RELEASE_CLOSE_PCT = 38.0    # lon hon muc nay -> xoa latch mo rieng ngon do

# V30: loc mem lai goi camera trong Vision Closing de thanh/truyen len Unity bot giat.
VISION_CLOSING_FOLLOW_ALPHA = 0.45

# ===================== PALM SENSOR SAFETY GATE =====================
# Yeu cau moi: cam bien FSR cua THUMB tren ESP32 khong dung cho ngon cai nua,
# ma duoc gan vao LONG BAN TAY. Python se lay FSR_N/FSR_PCT cua THUMB lam palm_contact.
# Khi chua co palm_contact, robot KHONG duoc gap/nam theo camera du da nhan duoc cam/trung.
# ===================== PALM SENSOR / SAFETY GATE =====================
# V55:
# - THUMB FSR khong con la palm sensor nua, ma la cam bien luc ngon cai.
# - Palm sensor/nut khoa duoc doc rieng tu ESP32 bang dong serial PALM_KEY CONTACT=0/1 RAW=...
# - Khi chua co palm_contact, robot KHONG duoc gap/nam theo camera du da nhan duoc vat.
USE_THUMB_FSR_AS_PALM_SENSOR = False
PALM_CONTACT_FORCE_N = 0.25   # chi dung khi muon fallback ve cach cu
PALM_CONTACT_PCT = 5.0        # chi dung khi muon fallback ve cach cu
PALM_KEY_STALE_SEC = 1.5

# V46/V47 ONE-TOUCH PALM UNLOCK:
# Sau khi YOLO da lock vat, chi can cam bien long ban tay cham 1 lan la mo khoa.
# Khong bat buoc phai giu FSR long ban tay lien tuc.
# V47: neu YOLO nhan sang vat KHAC, khoa lai va bat buoc cham Palm lai 1 lan.
PALM_ONE_TOUCH_UNLOCK_ENABLE = True

# Giu nho xung cham palm ngan. Neu vat vua duoc lock sat ngay sau khi cham palm,
# van cho unlock trong cua so thoi gian nay.
PALM_TOUCH_MEMORY_SEC = 1.20

# Web hien Palm Sensor ON khi da unlock, de nguoi demo thay trang thai mo khoa
# du da bo tay/vat khoi FSR long ban tay.
WEB_SHOW_PALM_UNLOCK_AS_SENSOR_ON = True

PALM_FORCE_DISABLE_THR_N = 30.0  # chi dung cho che do cu USE_THUMB_FSR_AS_PALM_SENSOR=True

# ===================== PALM SENSOR SOFTWARE TEST =====================
# Khong dung nut LOCK/UNLOCK nua.
# Bien nay tao tin hieu gia lap cam bien long ban tay de test phan mem khi chua cap phan cung.
# Bam nut PALM SENSOR TEST tren cua so camera de ON/OFF.
palm_sim_enabled = False

FSR_TARGET_MIN_N = 0.60
FSR_TARGET_MAX_N = 18.50

FSR_TARGET_BIAS_N = {
    # THUMB FSR khong con la cam bien ngon cai, no la PALM SENSOR nen khong chia target kep.
    "INDEX": 0.10,
    "MIDDLE": 0.10,
    "RING": -0.10,
    "PINKY": -0.10,
}

LOADCELL_FORCE_THR_SEND_MS = 360
LOADCELL_FORCE_THR_SEND_DELTA_N = 0.15
GRIP_SOFT_GUARD_GAIN_PCT_PER_N = 5.0



# ============================================================
# DATN EXTERNAL MODULES - YOLO 3 OBJECTS + AUTO GRIP STATE
# Dat 2 file sau cung thu muc voi code chinh:
# - datn_object_detector_yolo_3_separate.py
# - datn_auto_grip_state.py
# ============================================================

from datn_object_detector_yolo_3_separate import (
    ObjectResult,
    Yolo3SeparateObjectDetector,
)

from datn_auto_grip_state import (
    AutoGripStateMachine,
    VALID_OBJECT_LABELS,
)


# ===================== MEDIAPIPE INIT =====================
mp_hands = mp.solutions.hands
mp_draw = mp.solutions.drawing_utils

# UDP 5052: gui JSON tay/luc nhu code cu.
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

# UDP 5053: gui ten vat de Unity tu dong di chuyen object 3D.
object_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
last_unity_object_label_sent = None
last_unity_object_send_time = 0.0


def normalize_object_label_for_unity(label):
    """Chuan hoa label YOLO thanh chuoi Unity PythonObjectUdpLink.cs doc duoc."""
    if label is None:
        return "none"

    s = str(label).strip().lower()
    if s == "":
        return "none"

    # Detector YOLO 3 object hien tai.
    if s in ("kimquat_sauce", "kimquat", "kim_quat", "kim quất", "kim quat"):
        return "kimquat_sauce"

    if s in ("red_chili", "chili", "ot_bot", "ot bot", "ớt bột", "ot do", "ớt đỏ"):
        return "red_chili"

    if s in ("orange", "cam", "trai_cam", "trái cam"):
        return "orange"

    if s in ("none", "no_object", "no object", "null"):
        return "none"

    # Neu label la chuoi dai / JSON text thi van bat keyword.
    if "kimquat" in s or "kim_quat" in s or "kim qu" in s or "sauce" in s:
        return "kimquat_sauce"

    if "red_chili" in s or "chili" in s or "ot_bot" in s or "ớt" in s or "bot" in s or "bột":
        return "red_chili"

    if "orange" in s or "cam" in s:
        return "orange"

    return "none"


def send_object_to_unity(label, now_ts=None, force=False):
    """Gui object label sang Unity port 5053.

    Port 5052 van giu nguyen gui JSON tay/luc.
    Port 5053 chi gui plain text:
    - kimquat_sauce
    - red_chili
    - orange
    - none
    """
    global last_unity_object_label_sent, last_unity_object_send_time

    if now_ts is None:
        now_ts = time.time()

    unity_label = normalize_object_label_for_unity(label)

    # Chong spam UDP: gui khi doi label hoac moi 0.10s.
    if (
        not force
        and unity_label == last_unity_object_label_sent
        and (now_ts - last_unity_object_send_time) < UNITY_OBJECT_SEND_INTERVAL
    ):
        return

    try:
        object_sock.sendto(unity_label.encode("utf-8"), (UNITY_IP, UNITY_OBJECT_PORT))
        last_unity_object_label_sent = unity_label
        last_unity_object_send_time = now_ts
    except Exception as e:
        print("[UNITY OBJECT UDP ERROR]", e)


def create_hands_tracker():
    return mp_hands.Hands(
        static_image_mode=False,
        max_num_hands=1,
        model_complexity=MP_MODEL_COMPLEXITY,
        min_detection_confidence=MIN_DET_CONF,
        min_tracking_confidence=MIN_TRK_CONF,
    )


hands_main = create_hands_tracker()
hands_phone = create_hands_tracker()

# ===================== ORIGINAL ANGLE LOGIC =====================
JOINT_RANGES = {
    "thumb2": (170.0, 136.0),
    "thumb3": (176.0, 128.0),
    "index1": (176.0, 148.0),
    "index2": (176.0, 102.0),
    "index3": (176.0, 128.0),
    "middle1": (176.0, 148.0),
    "middle2": (176.0, 100.0),
    "middle3": (176.0, 126.0),
    "ring1": (176.0, 148.0),
    "ring2": (176.0, 100.0),
    "ring3": (176.0, 126.0),
    "pinky1": (176.0, 148.0),
    "pinky2": (176.0, 104.0),
    "pinky3": (176.0, 128.0),
}


def clamp(v, lo, hi):
    return max(lo, min(hi, v))


def lerp(a, b, t):
    return a + (b - a) * t


def softstep(v, edge0, edge1):
    if edge1 <= edge0:
        return 1.0 if v >= edge1 else 0.0
    t = clamp((v - edge0) / (edge1 - edge0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def curve_percent(v, power=1.0, scale=1.0, cap=100.0):
    x = clamp(v, 0.0, 100.0) / 100.0
    y = (x ** max(power, 1e-6)) * 100.0 * scale
    return clamp(y, 0.0, cap)


def unity_grip_boost(v, start, max_add):
    v = clamp(v, 0.0, 100.0)
    if v <= start:
        return v
    x = (v - start) / max(100.0 - start, 1e-6)
    return clamp(v + max_add * (x ** 1.35), 0.0, 100.0)


def vec_sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def vec_dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def vec_norm(v):
    return math.sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2])


def landmark_xyz(hand_landmarks, idx):
    p = hand_landmarks.landmark[idx]
    return (p.x, p.y, p.z)


def angle_3d(hand_landmarks, a_idx, b_idx, c_idx):
    a = landmark_xyz(hand_landmarks, a_idx)
    b = landmark_xyz(hand_landmarks, b_idx)
    c = landmark_xyz(hand_landmarks, c_idx)

    ba = vec_sub(a, b)
    bc = vec_sub(c, b)

    nba = vec_norm(ba)
    nbc = vec_norm(bc)
    if nba < 1e-8 or nbc < 1e-8:
        return 180.0

    cosang = vec_dot(ba, bc) / (nba * nbc)
    cosang = clamp(cosang, -1.0, 1.0)
    return math.degrees(math.acos(cosang))


def bend_percent_from_angle(angle_deg, straight_deg, bent_deg):
    if abs(straight_deg - bent_deg) < 1e-8:
        return 0.0
    t = (straight_deg - angle_deg) / (straight_deg - bent_deg)
    return clamp(t * 100.0, 0.0, 100.0)


def calibrate(v, offset=0.0, gain=1.0, deadzone=0.0):
    v = max(0.0, v - offset)
    if v < deadzone:
        return 0.0
    v = (v - deadzone) * gain
    return clamp(v, 0.0, 100.0)


def empty_packet():
    return {
        "thumb1": 0.0, "thumb2": 0.0, "thumb3": 0.0,
        "index1": 0.0, "index2": 0.0, "index3": 0.0,
        "middle1": 0.0, "middle2": 0.0, "middle3": 0.0,
        "ring1": 0.0, "ring2": 0.0, "ring3": 0.0,
        "pinky1": 0.0, "pinky2": 0.0, "pinky3": 0.0,
    }


def full_close_packet():
    """V9: lenh dong het hanh trinh logic 100%.

    Trong FORCE_HOLD, Python khong freeze toan ban tay nua.
    Python gui y dinh dong 100%, con ESP32 se tu dung RIENG TUNG NGON
    khi FSR cua ngon do dat nguong FORCE_THR_N.

    THUMB servo van dong 100% theo y dinh nam, nhung THUMB FSR khong duoc dung
    lam finger-force vi no da la cam bien long ban tay.
    """
    return {
        "thumb1": 100.0, "thumb2": 100.0, "thumb3": 100.0,
        "index1": 100.0, "index2": 100.0, "index3": 100.0,
        "middle1": 100.0, "middle2": 100.0, "middle3": 100.0,
        "ring1": 100.0, "ring2": 100.0, "ring3": 100.0,
        "pinky1": 100.0, "pinky2": 100.0, "pinky3": 100.0,
    }


def packet_copy(p):
    return {k: float(v) for k, v in p.items()}


def packet_diff(a, b):
    return max(abs(a[k] - b[k]) for k in a.keys())


def median3(a, b, c):
    return sorted([a, b, c])[1]


def median_list(vals):
    s = sorted(vals)
    return s[len(s) // 2]


def adaptive_alpha(prev_v, cur_v):
    d = abs(cur_v - prev_v)
    if d <= MOVE_THRESH:
        return EMA_ALPHA_SLOW
    if d >= FAST_THRESH:
        return EMA_ALPHA_FAST
    t = (d - MOVE_THRESH) / (FAST_THRESH - MOVE_THRESH)
    return EMA_ALPHA_SLOW + (EMA_ALPHA_FAST - EMA_ALPHA_SLOW) * t


def smooth_packet_adaptive(prev_packet, raw_packet):
    out = {}
    for k in raw_packet.keys():
        a = adaptive_alpha(prev_packet[k], raw_packet[k])
        out[k] = lerp(prev_packet[k], raw_packet[k], a)
    return out


def rate_limit_packet(prev_packet, cur_packet, dt):
    out = {}
    max_step = MAX_RATE_PER_SEC * max(dt, 1e-3)
    for k in cur_packet.keys():
        dv = cur_packet[k] - prev_packet[k]
        if dv > max_step:
            out[k] = prev_packet[k] + max_step
        elif dv < -max_step:
            out[k] = prev_packet[k] - max_step
        else:
            out[k] = cur_packet[k]
    return out


def apply_joint_hysteresis(packet, latch_state):
    out = {}
    for k, v in packet.items():
        if latch_state[k]:
            if v <= ZERO_ENTER:
                out[k] = 0.0
            else:
                latch_state[k] = False
                out[k] = v
        else:
            if v <= ZERO_EXIT:
                latch_state[k] = True
                out[k] = 0.0
            else:
                out[k] = v
    return out


def apply_finger_hysteresis(packet, finger_latch):
    out = dict(packet)
    groups = {
        "thumb": ("thumb1", "thumb2", "thumb3"),
        "index": ("index1", "index2", "index3"),
        "middle": ("middle1", "middle2", "middle3"),
        "ring": ("ring1", "ring2", "ring3"),
        "pinky": ("pinky1", "pinky2", "pinky3"),
    }
    for name, keys in groups.items():
        m = max(out[k] for k in keys)
        if finger_latch[name]:
            if m <= FINGER_ENTER:
                for k in keys:
                    out[k] = 0.0
            else:
                finger_latch[name] = False
        else:
            if m <= FINGER_EXIT:
                finger_latch[name] = True
                for k in keys:
                    out[k] = 0.0
    return out


def apply_open_suppression(packet):
    p = dict(packet)

    if p["thumb2"] < THUMB_HARD_ZERO_2 and p["thumb3"] < THUMB_HARD_ZERO_3:
        p["thumb1"] = 0.0
        p["thumb2"] = 0.0
        p["thumb3"] = 0.0
    else:
        s2 = softstep(p["thumb2"], THUMB_HARD_ZERO_2, THUMB_SOFT_ZERO_2)
        s3 = softstep(p["thumb3"], THUMB_HARD_ZERO_3, THUMB_SOFT_ZERO_3)
        s = max(s2, s3)
        if s < 1.0:
            p["thumb1"] *= s
            p["thumb2"] *= s
            p["thumb3"] *= s

    groups = [
        ("index1", "index2", "index3"),
        ("middle1", "middle2", "middle3"),
        ("ring1", "ring2", "ring3"),
        ("pinky1", "pinky2", "pinky3"),
    ]

    for k1, k2, k3 in groups:
        if p[k2] < FINGER_HARD_ZERO_2 and p[k3] < FINGER_HARD_ZERO_3:
            p[k1] = 0.0
            p[k2] = 0.0
            p[k3] = 0.0
        else:
            s2 = softstep(p[k2], FINGER_HARD_ZERO_2, FINGER_SOFT_ZERO_2)
            s3 = softstep(p[k3], FINGER_HARD_ZERO_3, FINGER_SOFT_ZERO_3)
            s = max(s2, s3)
            if s < 1.0:
                p[k1] *= s
                p[k2] *= s
                p[k3] *= s

    return p


def snap_packet_small_to_zero(packet, eps=FINAL_ZERO_SNAP):
    out = {}
    for k, v in packet.items():
        out[k] = 0.0 if abs(v) < eps else v
    return out


def compute_joint_packet(hand3d):
    """Calculate bend percentages and reject false bends in a straight pose.

    MediaPipe world-landmark Z is much noisier than the 2D overlay shown on
    screen.  Keep the original 3D mapping for real motion, but retain the
    measured angles so an obviously straight finger can be hard-snapped open
    before temporal filtering.
    """
    p = empty_packet()

    thumb_angle2 = angle_3d(hand3d, 1, 2, 3)
    thumb_angle3 = angle_3d(hand3d, 2, 3, 4)
    raw_thumb2 = bend_percent_from_angle(thumb_angle2, *JOINT_RANGES["thumb2"])
    raw_thumb3 = bend_percent_from_angle(thumb_angle3, *JOINT_RANGES["thumb3"])

    p["thumb2"] = calibrate(raw_thumb2, offset=5, gain=1.34, deadzone=2)
    p["thumb3"] = calibrate(raw_thumb3, offset=3, gain=1.42, deadzone=2)
    p["thumb1"] = clamp(max(0.82 * p["thumb2"], 0.62 * p["thumb3"]), 0.0, 100.0)
    p["thumb3"] = clamp(max(p["thumb3"], 0.88 * p["thumb2"] - 2.0), 0.0, 100.0)

    # FIX V56 - THUMB PALM-CLOSE BOOST:
    # MediaPipe joint-angle alone does not fully detect thumb opposition
    # because the thumb can move into the palm while the small thumb joints
    # still look relatively straight. Use thumb tip distance to palm MCPs
    # as an additional "thumb folded into palm" signal.
    try:
        wrist_p = landmark_xyz(hand3d, 0)
        index_mcp_p = landmark_xyz(hand3d, 5)
        middle_mcp_p = landmark_xyz(hand3d, 9)
        thumb_tip_p = landmark_xyz(hand3d, 4)

        palm_scale = max(vec_norm(vec_sub(middle_mcp_p, wrist_p)), 1e-6)
        thumb_to_index = vec_norm(vec_sub(thumb_tip_p, index_mcp_p)) / palm_scale
        thumb_to_middle = vec_norm(vec_sub(thumb_tip_p, middle_mcp_p)) / palm_scale
        thumb_to_palm = min(thumb_to_index, thumb_to_middle)

        # ratio <= 0.28: very close to palm => ~100%
        # ratio >= 0.72: open/outside palm => 0%
        thumb_palm_close = clamp((0.72 - thumb_to_palm) / max(0.72 - 0.28, 1e-6) * 100.0, 0.0, 100.0)

        # Only boost if thumb is clearly inside palm. Keep threshold high to avoid Hi/open-palm false close.
        if thumb_palm_close >= 48.0:
            p["thumb1"] = max(p["thumb1"], 0.82 * thumb_palm_close)
            p["thumb2"] = max(p["thumb2"], 0.90 * thumb_palm_close)
            p["thumb3"] = max(p["thumb3"], 0.98 * thumb_palm_close)
    except Exception:
        pass

    index_angle1 = angle_3d(hand3d, 0, 5, 6)
    index_angle2 = angle_3d(hand3d, 5, 6, 7)
    index_angle3 = angle_3d(hand3d, 6, 7, 8)
    raw_index1 = bend_percent_from_angle(index_angle1, *JOINT_RANGES["index1"])
    raw_index2 = bend_percent_from_angle(index_angle2, *JOINT_RANGES["index2"])
    raw_index3 = bend_percent_from_angle(index_angle3, *JOINT_RANGES["index3"])

    p["index1"] = calibrate(raw_index1, offset=8, gain=1.08, deadzone=5)
    p["index2"] = calibrate(raw_index2, offset=6, gain=1.15, deadzone=4)
    tip_index3 = calibrate(raw_index3, offset=3, gain=1.25, deadzone=3)
    p["index3"] = clamp(max(0.88 * p["index2"] + 0.08 * p["index1"], 0.92 * p["index2"] - 4.0), 0.0, 100.0)
    p["index3"] = min(p["index3"], max(tip_index3, 0.90 * p["index2"]))
    p["index1"] = clamp(max(p["index1"], 0.90 * p["index2"] - 2.0), 0.0, 100.0)

    middle_angle1 = angle_3d(hand3d, 0, 9, 10)
    middle_angle2 = angle_3d(hand3d, 9, 10, 11)
    middle_angle3 = angle_3d(hand3d, 10, 11, 12)
    raw_middle1 = bend_percent_from_angle(middle_angle1, *JOINT_RANGES["middle1"])
    raw_middle2 = bend_percent_from_angle(middle_angle2, *JOINT_RANGES["middle2"])
    raw_middle3 = bend_percent_from_angle(middle_angle3, *JOINT_RANGES["middle3"])

    p["middle1"] = calibrate(raw_middle1, offset=8, gain=1.08, deadzone=5)
    p["middle2"] = calibrate(raw_middle2, offset=6, gain=1.15, deadzone=4)
    tip_middle3 = calibrate(raw_middle3, offset=3, gain=1.25, deadzone=3)
    p["middle3"] = clamp(max(0.90 * p["middle2"] + 0.08 * p["middle1"], 0.94 * p["middle2"] - 4.0), 0.0, 100.0)
    p["middle3"] = min(p["middle3"], max(tip_middle3, 0.92 * p["middle2"]))
    p["middle1"] = clamp(max(p["middle1"], 0.92 * p["middle2"] - 2.0), 0.0, 100.0)

    ring_angle1 = angle_3d(hand3d, 0, 13, 14)
    ring_angle2 = angle_3d(hand3d, 13, 14, 15)
    ring_angle3 = angle_3d(hand3d, 14, 15, 16)
    raw_ring1 = bend_percent_from_angle(ring_angle1, *JOINT_RANGES["ring1"])
    raw_ring2 = bend_percent_from_angle(ring_angle2, *JOINT_RANGES["ring2"])
    raw_ring3 = bend_percent_from_angle(ring_angle3, *JOINT_RANGES["ring3"])

    p["ring1"] = calibrate(raw_ring1, offset=8, gain=1.08, deadzone=5)
    p["ring2"] = calibrate(raw_ring2, offset=6, gain=1.15, deadzone=4)
    tip_ring3 = calibrate(raw_ring3, offset=3, gain=1.25, deadzone=3)
    p["ring3"] = clamp(max(0.92 * p["ring2"] + 0.08 * p["ring1"], 0.96 * p["ring2"] - 4.0), 0.0, 100.0)
    p["ring3"] = min(p["ring3"], max(tip_ring3, 0.94 * p["ring2"]))
    p["ring1"] = clamp(max(p["ring1"], 0.94 * p["ring2"] - 2.0), 0.0, 100.0)

    pinky_angle1 = angle_3d(hand3d, 0, 17, 18)
    pinky_angle2 = angle_3d(hand3d, 17, 18, 19)
    pinky_angle3 = angle_3d(hand3d, 18, 19, 20)
    raw_pinky1 = bend_percent_from_angle(pinky_angle1, *JOINT_RANGES["pinky1"])
    raw_pinky2 = bend_percent_from_angle(pinky_angle2, *JOINT_RANGES["pinky2"])
    raw_pinky3 = bend_percent_from_angle(pinky_angle3, *JOINT_RANGES["pinky3"])

    p["pinky1"] = calibrate(raw_pinky1, offset=12, gain=1.00, deadzone=8)
    p["pinky2"] = calibrate(raw_pinky2, offset=11, gain=1.05, deadzone=7)
    p["pinky3"] = calibrate(raw_pinky3, offset=6, gain=1.12, deadzone=6)

    # Geometry-level open guard.  This happens before the old percentage
    # suppression, so a small Z-depth error cannot be amplified into 20-30%.
    if thumb_angle2 >= THUMB_GEOM_OPEN_MCP_DEG and thumb_angle3 >= THUMB_GEOM_OPEN_IP_DEG:
        p["thumb1"] = p["thumb2"] = p["thumb3"] = 0.0

    open_geometry = {
        "index": (index_angle1, index_angle2, index_angle3),
        "middle": (middle_angle1, middle_angle2, middle_angle3),
        "ring": (ring_angle1, ring_angle2, ring_angle3),
        "pinky": (pinky_angle1, pinky_angle2, pinky_angle3),
    }
    for finger_name, (a1, a2, a3) in open_geometry.items():
        if (
            a1 >= FINGER_GEOM_OPEN_MCP_DEG
            and a2 >= FINGER_GEOM_OPEN_PIP_DEG
            and a3 >= FINGER_GEOM_OPEN_DIP_DEG
        ):
            p[finger_name + "1"] = 0.0
            p[finger_name + "2"] = 0.0
            p[finger_name + "3"] = 0.0


    # FIX V57 - OPEN PALM / HI SIGN THUMB RESET:
    # Khi 4 ngon chinh dang mo gan het, coi day la ban tay mo.
    # Luc nay thumb khong duoc bi MediaPipe hieu nham thanh 40-50%.
    try:
        index_level_open_guard = max(p["index1"], p["index2"], p["index3"])
        middle_level_open_guard = max(p["middle1"], p["middle2"], p["middle3"])
        ring_level_open_guard = max(p["ring1"], p["ring2"], p["ring3"])
        pinky_level_open_guard = max(p["pinky1"], p["pinky2"], p["pinky3"])

        main4_max_open_guard = max(
            index_level_open_guard,
            middle_level_open_guard,
            ring_level_open_guard,
            pinky_level_open_guard,
        )

        # Neu dang ra dau Hi / ban tay mo: ep thumb ve 0.
        # Nguong 22 du de ring/pinky hoi rung nhe van tinh la open.
        if main4_max_open_guard <= 22.0:
            p["thumb1"] = 0.0
            p["thumb2"] = 0.0
            p["thumb3"] = 0.0
        else:
            # FIX V58 - ONE FINGER / GRIP THUMB CLOSE:
            # Truong hop hay gap khi demo:
            # - Chi ngón trỏ dang mo.
            # - Middle/Ring/Pinky dang gap cao.
            # MediaPipe thumb thuong chi ra 35-45%, lam robot thumb nam ky.
            # Neu 3 ngon con lai da gap, ep thumb dong sau hon de nhin giong tay that.
            closed_mid_ring_pinky = (
                middle_level_open_guard >= 45.0
                and ring_level_open_guard >= 45.0
                and pinky_level_open_guard >= 38.0
            )
            one_index_sign = index_level_open_guard <= 35.0 and closed_mid_ring_pinky

            closed_count_open_guard = 0
            for _lv in (
                index_level_open_guard,
                middle_level_open_guard,
                ring_level_open_guard,
                pinky_level_open_guard,
            ):
                if _lv >= 45.0:
                    closed_count_open_guard += 1

            if one_index_sign:
                thumb_force_pct = 88.0
            elif closed_count_open_guard >= 4:
                thumb_force_pct = 92.0
            elif closed_count_open_guard >= 3:
                thumb_force_pct = 82.0
            elif closed_count_open_guard >= 2:
                thumb_force_pct = 62.0
            else:
                thumb_force_pct = 0.0

            if thumb_force_pct > 0.0:
                p["thumb1"] = max(p["thumb1"], 0.82 * thumb_force_pct)
                p["thumb2"] = max(p["thumb2"], 0.92 * thumb_force_pct)
                p["thumb3"] = max(p["thumb3"], 1.00 * thumb_force_pct)
    except Exception:
        pass

    p = apply_open_suppression(p)
    return p

# ===================== OUTPUT SHAPING =====================
def compensate_motion_percent(name, motion_pct):
    cfg = SERVO_MECH_COMP.get(name)
    if not cfg:
        return clamp(motion_pct, 0.0, 100.0)

    v = clamp(motion_pct, 0.0, 100.0)
    start = float(cfg.get("start", 0.0))
    gain = float(cfg.get("gain", 1.0))
    offset = float(cfg.get("offset", 0.0))

    if v <= start:
        return v

    x = (v - start) / max(100.0 - start, 1e-6)
    boosted = start + (v - start) * gain + offset * (x * x)
    return clamp(boosted, 0.0, 100.0)


def shape_unity_finger(a, b, c, scales, caps, power, latch_name, latch_state):
    raw_max = max(a, b, c)

    if latch_state[latch_name]:
        if raw_max <= UNITY_FINGER_ENTER:
            return 0.0, 0.0, 0.0
        latch_state[latch_name] = False
    else:
        if raw_max <= UNITY_FINGER_EXIT:
            latch_state[latch_name] = True
            return 0.0, 0.0, 0.0

    aa = curve_percent(a, power=power, scale=UNITY_MASTER_SCALE * scales[0], cap=caps[0])
    bb = curve_percent(b, power=power, scale=UNITY_MASTER_SCALE * scales[1], cap=caps[1])
    cc = curve_percent(c, power=power, scale=UNITY_MASTER_SCALE * scales[2], cap=caps[2])

    aa = unity_grip_boost(aa, UNITY_GRIP_START + 6.0, 6.0)
    bb = unity_grip_boost(bb, UNITY_GRIP_START, 10.0)
    cc = unity_grip_boost(cc, UNITY_GRIP_START - 4.0, 12.0)

    aa = min(aa, bb * 1.12 + 7.0)
    bb = max(bb, 0.74 * aa)
    cc = max(cc, 0.86 * bb - 2.0)
    cc = min(cc, bb * 1.08 + 7.0)

    if aa < UNITY_JOINT_ZERO:
        aa = 0.0
    if bb < UNITY_JOINT_ZERO:
        bb = 0.0
    if cc < UNITY_JOINT_ZERO:
        cc = 0.0

    return aa, bb, cc


def build_unity_packet(robot_packet, unity_latch):
    u = empty_packet()

    u["thumb1"], u["thumb2"], u["thumb3"] = shape_unity_finger(
        robot_packet["thumb1"], robot_packet["thumb2"], robot_packet["thumb3"],
        UNITY_SCALES["THUMB"], UNITY_CAPS["THUMB"], UNITY_THUMB_CURVE_POWER,
        "thumb", unity_latch,
    )

    u["index1"], u["index2"], u["index3"] = shape_unity_finger(
        robot_packet["index1"], robot_packet["index2"], robot_packet["index3"],
        UNITY_SCALES["INDEX"], UNITY_CAPS["INDEX"], UNITY_CURVE_POWER,
        "index", unity_latch,
    )

    u["middle1"], u["middle2"], u["middle3"] = shape_unity_finger(
        robot_packet["middle1"], robot_packet["middle2"], robot_packet["middle3"],
        UNITY_SCALES["MIDDLE"], UNITY_CAPS["MIDDLE"], UNITY_CURVE_POWER,
        "middle", unity_latch,
    )

    u["ring1"], u["ring2"], u["ring3"] = shape_unity_finger(
        robot_packet["ring1"], robot_packet["ring2"], robot_packet["ring3"],
        UNITY_SCALES["RING"], UNITY_CAPS["RING"], UNITY_CURVE_POWER,
        "ring", unity_latch,
    )

    u["pinky1"], u["pinky2"], u["pinky3"] = shape_unity_finger(
        robot_packet["pinky1"], robot_packet["pinky2"], robot_packet["pinky3"],
        UNITY_SCALES["PINKY"], UNITY_CAPS["PINKY"], UNITY_CURVE_POWER,
        "pinky", unity_latch,
    )

    return snap_packet_small_to_zero(u, eps=UNITY_JOINT_ZERO)

# ===================== SERIAL / FSR =====================
def parse_named_values(text):
    out = {}
    for tok in text.replace(",", " ").split():
        if "=" not in tok:
            continue
        k, v = tok.split("=", 1)
        k = k.strip().upper()
        v = v.strip()
        if not k:
            continue
        try:
            out[k] = float(v)
        except Exception:
            pass
    return out


def loadcell_is_fresh(now_ts):
    if not LOADCELL_ENABLE:
        return False
    return (now_ts - last_loadcell_rx_time) <= LOADCELL_STALE_SEC


def loadcell_has_valid_object(now_ts):
    if not loadcell_is_fresh(now_ts):
        return False
    kg = clamp(loadcell_kg, 0.0, LOADCELL_MAX_KG)
    return kg >= LOADCELL_MIN_VALID_KG


def compute_target_grip_total_n(now_ts):
    """Tong luc kep muc tieu de giu vat khong roi."""
    if not loadcell_has_valid_object(now_ts):
        return 0.0

    mass_kg = clamp(loadcell_kg, 0.0, LOADCELL_MAX_KG)
    weight_n = mass_kg * 9.81
    target_total = weight_n * GRIP_SAFETY_FACTOR / max(GRIP_FRICTION_COEFF, 1e-6)
    return max(0.0, target_total)


def get_active_grip_fsr_names():
    """
    V55:
    Lay danh sach 5 ngon co FSR dang tac dong: THUMB/INDEX/MIDDLE/RING/PINKY.
    Palm sensor doc rieng, khong con dung THUMB FSR.
    """
    active = []
    for name in FORCE_CONTROL_FSR_NAMES:
        try:
            if fsr_contact_by_name(name, FSR_CONTACT_N, FSR_CONTACT_PCT):
                active.append(name)
        except Exception:
            pass
    return active


def compute_target_fsr_force_map(now_ts):
    """
    V55 - 5-FINGER TARGET FORCE MAP.

    - Tong luc muc tieu target_total duoc chia cho 5 ngon khi chua co ngon nao cham.
    - Khi co mot so ngon da cham vat, target moi ngon = target_total / so ngon dang cham.
    - Gia tri target hien thi/gui ESP32 duoc cap cho ca 5 ngon de ngon nao cham sau van co nguong dung luc.
    """
    total_n = compute_target_grip_total_n(now_ts)
    out = {name: 0.0 for name in FSR_NAMES}

    if total_n <= 0.0:
        return out

    active = get_active_grip_fsr_names()
    active_count = len(active)

    if active_count <= 0:
        each = total_n / max(float(len(FORCE_CONTROL_FSR_NAMES)), 1.0)
    else:
        each = total_n / max(float(active_count), 1.0)

    each = clamp(each, 0.0, FSR_TARGET_MAX_N)

    for name in FORCE_CONTROL_FSR_NAMES:
        out[name] = each

    return out


def compute_initial_fsr_force_map(now_ts):
    """
    Target hien thi ban dau tren web: chia deu tong luc cho 5 ngon kep.
    Gia tri nay khong nhay theo so FSR dang cham vat.
    """
    total_n = compute_target_grip_total_n(now_ts)
    out = {name: 0.0 for name in FSR_NAMES}

    if total_n <= 0.0:
        return out

    each = total_n / max(float(len(FORCE_CONTROL_FSR_NAMES)), 1.0)
    each = clamp(each, 0.0, FSR_TARGET_MAX_N)

    for name in FORCE_CONTROL_FSR_NAMES:
        out[name] = each

    return out


def safe_fsr_n(name):
    """Lay luc FSR theo Newton; neu chua co du lieu thi tra ve 0 de Unity khong bi hien -1."""
    try:
        v = fsr_force_n.get(name, -1.0)
        if v is None or v < 0.0:
            return 0.0
        return float(v)
    except Exception:
        return 0.0


def build_force_dashboard_packet(now_ts):
    """Goi du lieu mo rong gui sang Unity Force Analysis UI.
    Python van la noi tinh toan chinh; Unity chi nhan va hien thi.
    """
    mass_kg = clamp(loadcell_kg, 0.0, LOADCELL_MAX_KG)
    weight_n = mass_kg * 9.81

    recommended_n = compute_target_grip_total_n(now_ts)
    required_min_n = recommended_n * 0.85
    required_max_n = recommended_n * 1.15

    # V55: THUMB FSR la luc ngon cai that. Palm key doc rieng, khong tinh vao tong luc kep.
    thumb_force = safe_fsr_n("THUMB")
    index_force = safe_fsr_n("INDEX")
    middle_force = safe_fsr_n("MIDDLE")
    ring_force = safe_fsr_n("RING")
    pinky_force = safe_fsr_n("PINKY")
    palm_force = 0.0

    # Tong luc kep tinh du 5 ngon.
    total_force_n = thumb_force + index_force + middle_force + ring_force + pinky_force

    # Trang thai UI:
    # - Khi moi cham nhe / robot chua vao force-hold thi KHONG bao DANGER.
    # - Chi so sanh LOW/OPTIMAL/HIGH/DANGER khi grip_force_control_active = True.
    #   Như vậy chạm nhẹ FSR bị vọt ADC cũng không làm UI báo nguy hiểm giả.
    if mass_kg < LOADCELL_MIN_VALID_KG:
        status = "NO_OBJECT"
    elif recommended_n <= 0.0:
        status = "NO_TARGET"
    elif not grip_contact_detected:
        status = "WAIT_CONTACT"
    elif not grip_force_control_active:
        status = "WAIT_CONTACT"
    elif total_force_n < required_min_n:
        status = "LOW_GRIP"
    elif total_force_n <= required_max_n:
        status = "OPTIMAL_GRIP"
    elif total_force_n <= required_max_n * 1.30:
        status = "HIGH_GRIP"
    else:
        status = "DANGER_GRIP"

    force_ratio = 0.0
    if recommended_n > 0.001:
        force_ratio = total_force_n / recommended_n

    return {
        "mass_kg": round(mass_kg, 4),
        "mass_g": round(mass_kg * 1000.0, 1),
        "mu": float(GRIP_FRICTION_COEFF),
        "safety_factor": float(GRIP_SAFETY_FACTOR),
        "weight_n": round(weight_n, 3),

        "recommended_n": round(recommended_n, 3),
        "target_total_n": round(recommended_n, 3),
        "object_target_force_n": round(recommended_n, 3),
        "required_min_n": round(required_min_n, 3),
        "required_max_n": round(required_max_n, 3),

        "target_active_count": int(len(get_active_grip_fsr_names())),
        "target_active_fingers": ",".join(get_active_grip_fsr_names()),
        "target_mode": "ACTIVE_DYNAMIC_5FINGERS_DISPLAY" if len(get_active_grip_fsr_names()) > 0 else "INITIAL_EQUAL_5",

        "sensor_n": round(total_force_n, 3),
        "total_force_n": round(total_force_n, 3),

        "thumb_force": round(thumb_force, 3),
        "index_force": round(index_force, 3),
        "middle_force": round(middle_force, 3),
        "ring_force": round(ring_force, 3),
        "pinky_force": round(pinky_force, 3),
        "palm_force_n": round(palm_force, 3),
        "palm_contact": bool(palm_sensor_contact()),
        "thumb_is_palm_sensor": False,

        "thumb_target_n": round(grip_target_sensor_n.get("THUMB", 0.0), 3),
        "index_target_n": round(grip_target_sensor_n.get("INDEX", 0.0), 3),
        "middle_target_n": round(grip_target_sensor_n.get("MIDDLE", 0.0), 3),
        "ring_target_n": round(grip_target_sensor_n.get("RING", 0.0), 3),
        "pinky_target_n": round(grip_target_sensor_n.get("PINKY", 0.0), 3),

        "grip_active": bool(grip_force_control_active),
        "grip_contact": bool(grip_contact_detected),
        "force_ratio": round(force_ratio, 3),
        "status": status,
    }


def robot_close_level(packet):
    groups = {
        "THUMB": ("thumb1", "thumb2", "thumb3"),
        "INDEX": ("index1", "index2", "index3"),
        "MIDDLE": ("middle1", "middle2", "middle3"),
        "RING": ("ring1", "ring2", "ring3"),
        "PINKY": ("pinky1", "pinky2", "pinky3"),
    }

    vals = []
    for keys in groups.values():
        vals.append(sum(packet[k] for k in keys) / 3.0)

    return max(vals) if vals else 0.0


# ===================== V30 PER-FINGER RELEASE HELPERS =====================
FINGER_PACKET_KEYS = {
    "THUMB": ("thumb1", "thumb2", "thumb3"),
    "INDEX": ("index1", "index2", "index3"),
    "MIDDLE": ("middle1", "middle2", "middle3"),
    "RING": ("ring1", "ring2", "ring3"),
    "PINKY": ("pinky1", "pinky2", "pinky3"),
}


def finger_close_level(packet, finger_name):
    keys = FINGER_PACKET_KEYS.get(finger_name, ())
    if not keys:
        return 0.0
    vals = [float(packet.get(k, 0.0)) for k in keys]
    return sum(vals) / max(len(vals), 1)


def copy_one_finger(dst_packet, src_packet, finger_name):
    keys = FINGER_PACKET_KEYS.get(finger_name, ())
    for k in keys:
        dst_packet[k] = float(src_packet.get(k, 0.0))
    return dst_packet


def apply_force_hold_per_finger_release(base_packet, camera_packet, hand_detected, release_latch):
    """
    V30:
    Trong FORCE_HOLD mac dinh base_packet = full_close_packet().
    Neu camera van thay tay va nguoi dung mo rieng 1 ngon,
    ngon do se lay theo camera_packet; cac ngon khac van full close/giu luc.

    Luu y:
    - Day chi la lenh mo rieng tren Python/Unity/ESP32.
    - Actual force van duoc FSR + ESP32 FORCE_THR_N khong che.
    """
    if not FORCE_HOLD_PER_FINGER_RELEASE_ENABLE:
        return base_packet
    if not hand_detected or camera_packet is None:
        return base_packet

    out = packet_copy(base_packet)

    for fname in MANUAL_FINGER_ORDER:
        cam_level = finger_close_level(camera_packet, fname)

        if cam_level <= FINGER_RELEASE_OPEN_PCT:
            release_latch[fname] = True
        elif cam_level >= FINGER_RELEASE_CLOSE_PCT:
            release_latch[fname] = False

        if release_latch.get(fname, False):
            copy_one_finger(out, camera_packet, fname)

    return snap_packet_small_to_zero(out)


def fsr_contact_by_name(name, force_thr_n=FSR_CONTACT_N, pct_thr=FSR_CONTACT_PCT):
    if fsr_force_n.get(name, -1.0) >= force_thr_n:
        return True
    if fsr_pct.get(name, -1.0) >= pct_thr:
        return True
    return False


def palm_sensor_contact():
    """Palm key/sensor doc rieng tu ESP32; fallback cach cu neu can."""
    if USE_THUMB_FSR_AS_PALM_SENSOR:
        return fsr_contact_by_name(PALM_FSR_NAME, PALM_CONTACT_FORCE_N, PALM_CONTACT_PCT)

    # Neu co tin hieu PALM_KEY gan day thi dung tin hieu nay.
    if (time.time() - last_palm_key_rx_time) <= PALM_KEY_STALE_SEC:
        return bool(palm_key_contact)

    return False


def any_finger_fsr_contact():
    """Tinh du 5 FSR ngon that: THUMB/INDEX/MIDDLE/RING/PINKY."""
    for name in FINGER_FORCE_FSR_NAMES:
        if fsr_contact_by_name(name, FSR_CONTACT_N, FSR_CONTACT_PCT):
            return True
    return False


def any_fsr_contact():
    """Ham cu de debug tong quat: tinh ca palm + finger."""
    if palm_sensor_contact():
        return True
    return any_finger_fsr_contact()


def update_grip_force_state(now_ts, hand_detected, packet):
    """Bat dau giu luc khi: da can vat + camera/manu dang nam + FSR cham vat."""
    global grip_force_control_active, grip_contact_detected
    global grip_target_total_n, grip_target_sensor_n, grip_close_level_latched

    valid_load = loadcell_has_valid_object(now_ts)
    contact = any_finger_fsr_contact()
    close_level = robot_close_level(packet)

    if grip_close_level_latched:
        if close_level <= GRIP_CLOSE_LEVEL_EXIT:
            grip_close_level_latched = False
    else:
        if close_level >= GRIP_CLOSE_LEVEL_ENTER:
            grip_close_level_latched = True

    hand_ok = hand_detected if CONTROL_MODE == "AUTO" else True

    grip_contact_detected = bool(contact)
    grip_target_total_n = compute_target_grip_total_n(now_ts)
    grip_target_sensor_n = compute_target_fsr_force_map(now_ts)

    grip_force_control_active = bool(valid_load and hand_ok and grip_close_level_latched and contact)


def maybe_send_loadcell_force_thresholds(now_ts):
    """Gui target N xuong ESP32 khi da cham vat. ESP32 se stop servo bang FSR_N."""
    global last_force_thr_send_time, last_force_thr_sent

    if ser is None or not LOADCELL_ENABLE:
        return

    if not grip_force_control_active:
        return

    if (now_ts - last_force_thr_send_time) * 1000.0 < LOADCELL_FORCE_THR_SEND_MS:
        return

    thr_map = grip_target_sensor_n

    need_send = False

    # Gui target luc cho 5 FSR ngon kep, bao gom THUMB.
    for name in FORCE_CONTROL_FSR_NAMES:
        old = last_force_thr_sent.get(name, None)
        if old is None or abs(thr_map.get(name, 0.0) - old) >= LOADCELL_FORCE_THR_SEND_DELTA_N:
            need_send = True
            break

    if not need_send:
        return

    serial_send_line("FORCE_MODE_N")

    for name in FORCE_CONTROL_FSR_NAMES:
        serial_send_line(f"FORCE_THR_N {name} {thr_map.get(name, 0.0):.2f}")
        last_force_thr_sent[name] = thr_map.get(name, 0.0)

    last_force_thr_send_time = now_ts


def maybe_disable_force_thresholds_for_direct_follow(now_ts):
    """V45: Khi dang follow tay sau Palm, day nguong FSR len cao.

    Muc tieu: khong de ESP32 force-hit latch lam servo khong mo/khong dong
    khi nguoi dung dang demo nắm/mở theo camera. Van dung lenh FORCE_THR_N
    san co nen khong can sua ESP32.
    """
    global last_direct_follow_force_disable_time, last_force_thr_sent

    if ser is None:
        return

    if (now_ts - last_direct_follow_force_disable_time) < DIRECT_FOLLOW_FORCE_DISABLE_INTERVAL_SEC:
        return

    serial_send_line("FORCE_MODE_N")
    for name in FSR_NAMES:
        serial_send_line(f"FORCE_THR_N {name} {DIRECT_FOLLOW_DISABLE_FORCE_LIMIT_N:.2f}")
        last_force_thr_sent[name] = DIRECT_FOLLOW_DISABLE_FORCE_LIMIT_N

    last_direct_follow_force_disable_time = now_ts


def apply_fsr_soft_guard(name, cmd_pct, now_ts, fsr_pct_map, last_fsr_rx_ts):
    # V55: ca THUMB cung co FSR luc rieng, nen duoc phep tham gia soft guard neu bat FSR_SOFT_GUARD_ENABLE.
    if not FSR_SOFT_GUARD_ENABLE:
        return cmd_pct
    if (now_ts - last_fsr_rx_ts) > FSR_STALE_SEC:
        return cmd_pct

    # Uu tien che do moi: FSR_N >= target_N thi giam lenh kep phia Python de mem hon.
    if grip_force_control_active and name in grip_target_sensor_n:
        target_n = grip_target_sensor_n.get(name, 0.0)
        force_n = fsr_force_n.get(name, -1.0)
        if target_n > 0.0 and force_n >= 0.0:
            if force_n <= target_n:
                return cmd_pct

            excess_n = force_n - target_n
            relaxed = cmd_pct - excess_n * GRIP_SOFT_GUARD_GAIN_PCT_PER_N
            return clamp(relaxed, 0.0, 100.0)

    # Fallback cu: dung FSR% neu chua co loadcell/FSR_N.
    force_pct = fsr_pct_map.get(name, -1.0)
    if force_pct < 0.0:
        return cmd_pct

    thr = FSR_SOFT_GUARD_THRESH.get(name, 100.0)
    if force_pct <= thr:
        return cmd_pct

    excess = force_pct - thr
    relaxed = cmd_pct - excess * FSR_SOFT_GUARD_GAIN
    return clamp(relaxed, 0.0, 100.0)


def build_serial_percent(packet, name, now_ts, fsr_pct_map, last_fsr_rx_ts):
    if name == "INDEX":
        v = 0.15 * packet["index1"] + 0.50 * packet["index2"] + 0.35 * packet["index3"]
    elif name == "MIDDLE":
        v = 0.15 * packet["middle1"] + 0.50 * packet["middle2"] + 0.35 * packet["middle3"]
    elif name == "RING":
        v = 0.15 * packet["ring1"] + 0.50 * packet["ring2"] + 0.35 * packet["ring3"]
    elif name == "PINKY":
        v = 0.18 * packet["pinky1"] + 0.47 * packet["pinky2"] + 0.35 * packet["pinky3"]
    elif name == "THUMB":
        v = 0.20 * packet["thumb1"] + 0.40 * packet["thumb2"] + 0.40 * packet["thumb3"]
        # FIX V56: push thumb deeper once it is clearly closing.
        if v >= 35.0:
            v = v * 1.18 + 6.0
    else:
        v = 0.0

    v = compensate_motion_percent(name, v)
    v = apply_fsr_soft_guard(name, v, now_ts, fsr_pct_map, last_fsr_rx_ts)
    pct = int(clamp(v, 0.0, 100.0) + 0.5)

    if SERVO_INVERT.get(name, False):
        pct = 100 - pct

    return pct


def web_finger_command_pct(packet, name, now_ts):
    """Lay % lenh hien tai de dua len Page 1 Auto Command Bars."""
    try:
        return float(build_serial_percent(packet, name, now_ts, fsr_pct, last_fsr_rx_time))
    except Exception:
        try:
            mapping = {
                "THUMB": ("thumb1", "thumb2", "thumb3"),
                "INDEX": ("index1", "index2", "index3"),
                "MIDDLE": ("middle1", "middle2", "middle3"),
                "RING": ("ring1", "ring2", "ring3"),
                "PINKY": ("pinky1", "pinky2", "pinky3"),
            }
            keys = mapping.get(name, ())
            return float(sum(packet.get(k, 0.0) for k in keys) / max(len(keys), 1))
        except Exception:
            return 0.0



def web_finger_raw_pct(packet, name):
    """% goc ban tay/gesture goc, truoc khi shape sang Unity."""
    try:
        mapping = {
            "THUMB": ("thumb1", "thumb2", "thumb3"),
            "INDEX": ("index1", "index2", "index3"),
            "MIDDLE": ("middle1", "middle2", "middle3"),
            "RING": ("ring1", "ring2", "ring3"),
            "PINKY": ("pinky1", "pinky2", "pinky3"),
        }
        keys = mapping.get(name, ())
        return float(sum(packet.get(k, 0.0) for k in keys) / max(len(keys), 1))
    except Exception:
        return 0.0


def web_finger_unity_pct(unity_packet, name):
    """% goc Unity simulated: lay trung binh 3 khop da shape gui sang Unity."""
    try:
        mapping = {
            "THUMB": ("thumb1", "thumb2", "thumb3"),
            "INDEX": ("index1", "index2", "index3"),
            "MIDDLE": ("middle1", "middle2", "middle3"),
            "RING": ("ring1", "ring2", "ring3"),
            "PINKY": ("pinky1", "pinky2", "pinky3"),
        }
        keys = mapping.get(name, ())
        return float(sum(unity_packet.get(k, 0.0) for k in keys) / max(len(keys), 1))
    except Exception:
        return 0.0

def servo_postprocess(name, pct, prev_sent, filt_hist, filt_ema):
    filt_hist[name].append(pct)
    med = median_list(list(filt_hist[name]))

    if filt_ema[name] < 0:
        filt_ema[name] = float(med)
    else:
        filt_ema[name] = lerp(filt_ema[name], float(med), SERIAL_EMA_ALPHA)

    out = int(filt_ema[name] + 0.5)

    if out <= SERIAL_SNAP_LOW:
        out = 0
    elif out >= SERIAL_SNAP_HIGH:
        out = 100

    if prev_sent >= 0 and abs(out - prev_sent) <= SERIAL_DEADBAND:
        out = prev_sent

    if SERIAL_QUANT_STEP > 1:
        out = int(round(out / SERIAL_QUANT_STEP) * SERIAL_QUANT_STEP)

    return int(clamp(out, 0, 100))


# ===================== AUTO / MANU CLICK UI HELPERS =====================
def move_toward_value(cur, tgt, step):
    if cur < tgt - step:
        return cur + step
    if cur > tgt + step:
        return cur - step
    return tgt


def update_manual_percent_smooth(target_pct, current_pct, dt):
    """Lam mem slider de servo khong giat khi nguoi dung keo nhanh."""
    step = MANUAL_MAX_PCT_PER_SEC * max(dt, 1e-3)
    out = {}
    for name in MANUAL_FINGER_ORDER:
        cur = current_pct.get(name, 0.0)
        tgt = clamp(target_pct.get(name, 0.0), 0.0, 100.0)
        out[name] = move_toward_value(cur, tgt, step)
    return out


def build_robot_packet_from_manual(manual_pct):
    """Chuyen 5 slider thanh packet 15 khop de Unity va ESP32 dung chung pipeline hien tai."""
    p = empty_packet()
    mapping = {
        "THUMB": ("thumb1", "thumb2", "thumb3"),
        "INDEX": ("index1", "index2", "index3"),
        "MIDDLE": ("middle1", "middle2", "middle3"),
        "RING": ("ring1", "ring2", "ring3"),
        "PINKY": ("pinky1", "pinky2", "pinky3"),
    }
    for name, keys in mapping.items():
        v = clamp(manual_pct.get(name, 0.0), 0.0, 100.0)
        for k in keys:
            p[k] = v
    return p


def reset_hist_to_packet(hist_dict, packet):
    """Khi dang MANU, reset median history de luc quay lai AUTO khong bi keo theo du lieu cu."""
    for k in hist_dict.keys():
        hist_dict[k].clear()
        hist_dict[k].extend([packet[k], packet[k], packet[k]])


def add_ui_hitbox(kind, x1, y1, x2, y2, name=None, value=None):
    ui_hitboxes.append({
        "kind": kind,
        "x1": int(x1), "y1": int(y1),
        "x2": int(x2), "y2": int(y2),
        "name": name,
        "value": value,
    })


def hitbox_contains(hb, x, y):
    return hb["x1"] <= x <= hb["x2"] and hb["y1"] <= y <= hb["y2"]


def set_manual_slider_from_mouse(name, x_full):
    global manual_target_pct

    for hb in reversed(ui_hitboxes):
        if hb.get("kind") == "slider" and hb.get("name") == name:
            x1 = hb["x1"]
            x2 = hb["x2"]
            v = (x_full - x1) * 100.0 / max(float(x2 - x1), 1.0)
            manual_target_pct[name] = clamp(v, 0.0, 100.0)
            return


def dashboard_mouse_event(event, x, y, flags, param):
    """Dieu khien bang chuot truc tiep tren man hinh dashboard da scale nho."""
    global CONTROL_MODE, ui_drag_finger, manual_target_pct
    global manual_load_input_active, manual_load_input_text
    global manual_loadcell_override, last_serial_note
    global palm_sim_enabled, palm_key_unlocked

    # Imshow dang hien display_small, nen quy doi ve toa do display goc.
    x_full = int(x / max(PY_WINDOW_SCALE, 1e-6))
    y_full = int(y / max(PY_WINDOW_SCALE, 1e-6))

    if event == cv2.EVENT_LBUTTONDOWN:
        ui_drag_finger = None

        for hb in reversed(ui_hitboxes):
            if not hitbox_contains(hb, x_full, y_full):
                continue

            kind = hb.get("kind")
            if kind == "mode":
                CONTROL_MODE = str(hb.get("value", "AUTO"))
                return

            if kind == "reset":
                for name in MANUAL_FINGER_ORDER:
                    manual_target_pct[name] = 0.0
                return

            if kind == "palm_sim_toggle":
                # Khoa cheo: neu cam bien long ban tay that dang ON thi khong cho bat PALM TEST.
                # Muc tieu: tranh nut test lam anh huong qua trinh khi phan cung dang cham that.
                if palm_sensor_contact():
                    palm_sim_enabled = False
                    palm_key_unlocked = True
                    last_serial_note = "PALM HW ACTIVE - TEST LOCKED"
                    print("[PALM SIM LOCKED] Hardware palm sensor is ON")
                    return

                palm_sim_enabled = not palm_sim_enabled
                if not palm_sim_enabled:
                    # OFF nghia la bo gia lap tin hieu long ban tay va khoa lai latch test.
                    palm_key_unlocked = False
                last_serial_note = "PALM SIM ON" if palm_sim_enabled else "PALM SIM OFF"
                print("[PALM SIM]", "ON" if palm_sim_enabled else "OFF")
                return

            if kind == "load_input":
                manual_load_input_active = True
                manual_load_input_text = ""
                return

            if kind == "load_real":
                manual_loadcell_override = False
                manual_load_input_active = False
                manual_load_input_text = ""
                last_serial_note = "MANUAL LOAD OFF"
                print("[MANUAL LOAD] OFF - quay lai loadcell that")
                return

            if kind == "slider":
                CONTROL_MODE = "MANU"
                ui_drag_finger = hb.get("name")
                set_manual_slider_from_mouse(ui_drag_finger, x_full)
                return

    elif event == cv2.EVENT_MOUSEMOVE:
        if ui_drag_finger is not None and (flags & cv2.EVENT_FLAG_LBUTTON):
            set_manual_slider_from_mouse(ui_drag_finger, x_full)

    elif event == cv2.EVENT_LBUTTONUP:
        if ui_drag_finger is not None:
            set_manual_slider_from_mouse(ui_drag_finger, x_full)
        ui_drag_finger = None


def draw_click_button(img, x, y, w, h, text, active=False, kind="mode", value=None):
    bg = (35, 85, 55) if active else (38, 48, 72)
    border = (80, 255, 120) if active else (31, 214, 255)
    fg = (220, 255, 225) if active else (220, 240, 255)

    cv2.rectangle(img, (x, y), (x + w, y + h), bg, -1)
    cv2.rectangle(img, (x, y), (x + w, y + h), border, 2)

    prefix = "[X] " if active else "[ ] "
    put_line(img, prefix + text, x + 8, y + h - 9, fg, scale=0.48, thick=1)

    add_ui_hitbox(kind, x, y, x + w, y + h, value=value)


def draw_palm_sim_button(img, x, y, w, h):
    """Nut gia lap tin hieu cam bien long ban tay, dat duoi o nhap gram."""
    active = bool(palm_sim_enabled)
    bg = (25, 70, 42) if active else (34, 44, 65)
    border = (80, 255, 120) if active else (31, 214, 255)
    fg = (220, 255, 225) if active else (220, 235, 255)
    lamp_col = (70, 255, 110) if active else (55, 65, 80)
    lamp_border = (180, 255, 190) if active else (120, 130, 150)

    cv2.rectangle(img, (x, y), (x + w, y + h), bg, -1)
    cv2.rectangle(img, (x, y), (x + w, y + h), border, 2)

    cy = y + h // 2
    cv2.circle(img, (x + 18, cy), 9, lamp_col, -1)
    cv2.circle(img, (x + 18, cy), 9, lamp_border, 2)

    state = "ON" if active else "OFF"
    put_line(img, "PALM SENSOR TEST", x + 36, y + 17, fg, scale=0.42, thick=1)
    put_line(img, state, x + w - 54, y + 17, (80, 255, 120) if active else (180, 190, 210), scale=0.48, thick=2)

    add_ui_hitbox("palm_sim_toggle", x, y, x + w, y + h)


def draw_manual_load_input_box(img, x, y, w, h):
    """O nhap khoi luong manual, dat trong khung UNITY OUTPUT."""
    bg = (25, 35, 55)
    border = (80, 255, 120) if manual_load_input_active else (31, 214, 255)

    cv2.rectangle(img, (x, y), (x + w, y + h), bg, -1)
    cv2.rectangle(img, (x, y), (x + w, y + h), border, 2)

    if manual_load_input_active:
        title = "NHAP GRAM + ENTER"
        shown = manual_load_input_text + "_"
        value_col = (80, 255, 120)
    else:
        title = "CLICK DE NHAP KHOI LUONG"
        shown = f"{manual_loadcell_g:.0f}"
        value_col = (80, 255, 120) if manual_loadcell_override else (180, 220, 255)

    put_line(img, title, x + 8, y + 14, (180, 235, 255), scale=0.36, thick=1)
    put_line(img, shown + " g", x + 8, y + 38, value_col, scale=0.64, thick=2)

    add_ui_hitbox("load_input", x, y, x + w, y + h)


def draw_manual_slider_row(img, x, y, w, name, target_pct, current_pct):
    label_w = 78
    track_x = x + label_w
    track_y = y
    track_w = w - label_w - 48
    track_h = 12

    target_pct = clamp(target_pct, 0.0, 100.0)
    current_pct = clamp(current_pct, 0.0, 100.0)

    put_line(img, name[:6], x, y + 5, (230, 245, 255), scale=0.43, thick=1)

    # Nen track
    cv2.rectangle(img, (track_x, track_y - track_h // 2), (track_x + track_w, track_y + track_h // 2), (32, 45, 72), -1)
    cv2.rectangle(img, (track_x, track_y - track_h // 2), (track_x + track_w, track_y + track_h // 2), (92, 120, 155), 1)

    # Current output chay mem
    cur_w = int(track_w * current_pct / 100.0)
    if cur_w > 0:
        cv2.rectangle(img, (track_x, track_y - track_h // 2), (track_x + cur_w, track_y + track_h // 2), (51, 255, 153), -1)

    # Target knob
    knob_x = track_x + int(track_w * target_pct / 100.0)
    cv2.circle(img, (knob_x, track_y), 8, (255, 216, 77), -1)
    cv2.circle(img, (knob_x, track_y), 8, (20, 20, 20), 1)

    put_line(img, f"{int(current_pct + 0.5):03d}", track_x + track_w + 10, y + 5, (255, 216, 77), scale=0.42, thick=1)

    # Hitbox lon hon track de de bam/keo
    add_ui_hitbox("slider", track_x, track_y - 13, track_x + track_w, track_y + 13, name=name)


def draw_dashboard_controls(img, sts_x, panel_y, panel_w, control_mode, target_pct, current_pct):
    """Chi ve nut AUTO/MANU/RESET trong SYSTEM STATUS.
    5 slider manual duoc dua qua khung ROBOT HAND DATA de khong de len LOAD/TGT/FSR.
    """
    x = sts_x + 14
    y = panel_y + 72

    draw_click_button(img, x, y, 94, 28, "AUTO", active=(control_mode == "AUTO"), kind="mode", value="AUTO")
    draw_click_button(img, x + 102, y, 94, 28, "MANU", active=(control_mode == "MANU"), kind="mode", value="MANU")
    draw_click_button(img, x + 204, y, 96, 28, "RESET", active=False, kind="reset", value="RESET")

    mode_text = "AUTO: camera detect" if control_mode == "AUTO" else "MANU: keo slider o khung ben trai"
    palm_text = "PALM SIM ON" if palm_sim_enabled else "PALM SIM OFF"
    put_line(img, mode_text + " | " + palm_text, x, y + 50, (80, 255, 120) if control_mode == "AUTO" else (0, 220, 255), scale=0.43, thick=1)


def draw_manual_sliders_block(img, x, y, w, target_pct, current_pct):
    """Ve 5 slider manual trong vung trong cua khung ROBOT HAND DATA."""
    cv2.rectangle(img, (x, y), (x + w, y + 154), (24, 36, 56), -1)
    cv2.rectangle(img, (x, y), (x + w, y + 154), (31, 214, 255), 1)
    put_line(img, "MANUAL FINGER SLIDERS", x + 8, y + 18, (80, 255, 120), scale=0.45, thick=1)
    put_line(img, "click/keo thanh - tu dong chuyen MANU", x + 8, y + 38, (180, 235, 255), scale=0.34, thick=1)

    slider_x = x + 8
    slider_y = y + 62
    slider_w = w - 16
    row_gap = 19
    for i, name in enumerate(MANUAL_FINGER_ORDER):
        draw_manual_slider_row(
            img,
            slider_x,
            slider_y + i * row_gap,
            slider_w,
            name,
            target_pct.get(name, 0.0),
            current_pct.get(name, 0.0),
        )

# ===================== UI - DASHBOARD FROM CODE 2 =====================
# ===================== UI - DASHBOARD FROM CODE 2 =====================
def put_line(img, text, x, y, color=(0, 255, 0), scale=0.55, thick=2):
    cv2.putText(img, text, (x, y), cv2.FONT_HERSHEY_SIMPLEX, scale, (10, 10, 10), thick + 2, cv2.LINE_AA)
    cv2.putText(img, text, (x, y), cv2.FONT_HERSHEY_SIMPLEX, scale, color, thick, cv2.LINE_AA)


def draw_panel(img, x, y, w, h, title, border=(31, 214, 255), fill=(18, 26, 43), title_color=(255, 216, 77)):
    cv2.rectangle(img, (x, y), (x + w, y + h), fill, -1)
    cv2.rectangle(img, (x, y), (x + w, y + h), border, 2)
    cv2.rectangle(img, (x, y), (x + w, y + 34), (22, 35, 58), -1)
    cv2.line(img, (x, y + 34), (x + w, y + 34), border, 1)
    put_line(img, title, x + 12, y + 24, title_color, scale=0.62, thick=2)


def draw_badge(img, x, y, text, bg, fg=(255, 255, 255), pad_x=10, h=26):
    w = max(70, int(len(text) * 9 + 2 * pad_x))
    cv2.rectangle(img, (x, y), (x + w, y + h), bg, -1)
    cv2.rectangle(img, (x, y), (x + w, y + h), (80, 80, 80), 1)
    put_line(img, text, x + pad_x, y + 18, fg, scale=0.48, thick=1)
    return x + w + 8


def draw_progress_row(img, x, y, w, label, values, bar_color=(51, 255, 153), text_color=(234, 246, 255)):
    v1, v2, v3 = [int(clamp(v, 0, 100)) for v in values]
    avg = int((v1 + v2 + v3) / 3.0 + 0.5)
    put_line(img, f"{label:<6} {v1:>3} {v2:>3} {v3:>3}", x, y, text_color, scale=0.58, thick=2)
    bar_x = x + 180
    bar_y = y - 14
    bar_w = max(60, w - 210)
    cv2.rectangle(img, (bar_x, bar_y), (bar_x + bar_w, bar_y + 12), (34, 47, 76), -1)
    fill_w = int(bar_w * avg / 100.0)
    if fill_w > 0:
        cv2.rectangle(img, (bar_x, bar_y), (bar_x + fill_w, bar_y + 12), bar_color, -1)
    cv2.rectangle(img, (bar_x, bar_y), (bar_x + bar_w, bar_y + 12), (95, 120, 150), 1)


def draw_status_line(img, x, y, label, value, color=(234, 246, 255)):
    put_line(img, f"{label}: {value}", x, y, color, scale=0.58, thick=2)


def draw_camera_panel_bg(display):
    display[:] = (11, 16, 32)
    cv2.rectangle(display, (0, 0), (WINDOW_W - 1, WINDOW_H - 1), (22, 35, 58), 2)


def fit_to_box(img, target_w, target_h):
    out = np.zeros((target_h, target_w, 3), dtype=np.uint8)
    if img is None:
        return out

    h, w = img.shape[:2]
    if h <= 0 or w <= 0:
        return out

    scale = min(target_w / float(w), target_h / float(h))
    new_w = max(1, int(w * scale))
    new_h = max(1, int(h * scale))
    resized = cv2.resize(img, (new_w, new_h), interpolation=cv2.INTER_AREA)

    x0 = (target_w - new_w) // 2
    y0 = (target_h - new_h) // 2
    out[y0:y0 + new_h, x0:x0 + new_w] = resized
    return out


def mark_camera_frame(frame, label, active=False, detected=False):
    if frame is None:
        frame = np.zeros((FRAME_H, FRAME_W, 3), dtype=np.uint8)
        put_line(frame, f"{label}: NO SIGNAL", 20, 40, (0, 0, 255))
        return frame

    color = (0, 255, 0) if active else (180, 180, 180)
    status = "HAND OK" if detected else "NO HAND"
    put_line(frame, f"{label} | {status}" + (" | ACTIVE" if active else ""), 20, 30, color)

    if active:
        cv2.rectangle(frame, (4, 4), (frame.shape[1] - 5, frame.shape[0] - 5), (0, 255, 0), 3)
    return frame


def build_dashboard(frame_main, frame_phone, active_cam, main_detected, phone_detected):
    display = np.zeros((WINDOW_H, WINDOW_W, 3), dtype=np.uint8)
    draw_camera_panel_bg(display)

    cam_gap = 12
    cam_w = CAM_VIEW_W - cam_gap
    cam_h = TOP_VIEW_H - 20
    left_x = 10
    right_x = CAM_VIEW_W + 2
    cam_y = 10

    main_view = mark_camera_frame(
        frame_main.copy() if frame_main is not None else None,
        "MAIN CAM",
        active=(active_cam == "MAIN"),
        detected=main_detected,
    )
    phone_view = mark_camera_frame(
        frame_phone.copy() if frame_phone is not None else None,
        CAM_PHONE_NAME,
        active=(active_cam == "PHONE"),
        detected=phone_detected,
    )

    main_view = fit_to_box(main_view, cam_w - 4, cam_h - 38)
    phone_view = fit_to_box(phone_view, cam_w - 4, cam_h - 38)

    border_main = (51, 255, 153) if active_cam == "MAIN" else (31, 214, 255)
    draw_panel(display, left_x, cam_y, cam_w, cam_h, "MAIN CAMERA", border=border_main)
    display[cam_y + 36:cam_y + 36 + main_view.shape[0], left_x + 2:left_x + 2 + main_view.shape[1]] = main_view

    border_phone = (51, 255, 153) if active_cam == "PHONE" else (31, 214, 255)
    draw_panel(display, right_x, cam_y, cam_w, cam_h, "PHONE CAMERA", border=border_phone)
    display[cam_y + 36:cam_y + 36 + phone_view.shape[0], right_x + 2:right_x + 2 + phone_view.shape[1]] = phone_view

    next_x = left_x + 190
    next_x = draw_badge(display, next_x, cam_y + 6, "ACTIVE" if active_cam == "MAIN" else "STANDBY", (35, 70, 45) if active_cam == "MAIN" else (45, 55, 75))
    draw_badge(display, next_x, cam_y + 6, "HAND OK" if main_detected else "NO HAND", (30, 90, 60) if main_detected else (65, 50, 40))

    next_x = right_x + 205
    next_x = draw_badge(display, next_x, cam_y + 6, "ACTIVE" if active_cam == "PHONE" else "STANDBY", (35, 70, 45) if active_cam == "PHONE" else (45, 55, 75))
    draw_badge(display, next_x, cam_y + 6, "HAND OK" if phone_detected else "NO HAND", (30, 90, 60) if phone_detected else (65, 50, 40))

    info_y = TOP_VIEW_H + 8
    return display, 18, info_y



def build_compact_camera_ui(frame_main, frame_phone, active_cam, main_detected, phone_detected, palm_raw, palm_sim, obj_label="none"):
    """
    V25:
    - Hien 2 camera gon hon.
    - Bo nut PALM SENSOR TEST to o duoi.
    - Dua nut PALM TEST nho len thanh header, nam gan OBJ.
    """
    display = np.zeros((WINDOW_H, WINDOW_W, 3), dtype=np.uint8)
    display[:] = (7, 12, 22)

    # Nen luoi nhe
    for x in range(0, WINDOW_W, 32):
        cv2.line(display, (x, 0), (x, WINDOW_H), (13, 26, 40), 1)
    for y in range(0, WINDOW_H, 32):
        cv2.line(display, (0, y), (WINDOW_W, y), (13, 26, 40), 1)

    # Header gon
    cv2.rectangle(display, (0, 0), (WINDOW_W, 42), (10, 18, 32), -1)
    cv2.line(display, (0, 42), (WINDOW_W, 42), (31, 214, 255), 1)
    put_line(display, "DATN_BAN_THU_GON", 12, 26, (255, 235, 40), scale=0.54, thick=2)
    put_line(display, "BOTH CAM DETECT", 225, 26, (80, 255, 120), scale=0.40, thick=1)

    obj_text = str(obj_label).upper()[:16]
    put_line(display, f"OBJ {obj_text}", 385, 26, (31, 214, 255), scale=0.40, thick=1)

    # Nut PALM TEST nho tren header, thay cho nut lon o duoi
    btn_w, btn_h = 132, 28
    btn_x, btn_y = WINDOW_W - btn_w - 10, 7
    active = bool(palm_sim_enabled)
    locked = bool(palm_sensor_contact())
    bg = (25, 70, 42) if active else (34, 44, 65)
    border = (80, 255, 120) if active else ((120, 130, 150) if locked else (31, 214, 255))
    lamp_col = (70, 255, 110) if active else ((75, 75, 75) if locked else (55, 65, 80))
    state = "LOCK" if locked else ("ON" if active else "OFF")

    cv2.rectangle(display, (btn_x, btn_y), (btn_x + btn_w, btn_y + btn_h), bg, -1)
    cv2.rectangle(display, (btn_x, btn_y), (btn_x + btn_w, btn_y + btn_h), border, 2)
    cv2.circle(display, (btn_x + 14, btn_y + btn_h // 2), 7, lamp_col, -1)
    cv2.circle(display, (btn_x + 14, btn_y + btn_h // 2), 7, (180, 255, 190) if active else (110, 120, 135), 1)
    put_line(display, "PALM TEST", btn_x + 28, btn_y + 18, (220, 245, 255), scale=0.34, thick=1)
    put_line(display, state, btn_x + btn_w - 38, btn_y + 18, (80, 255, 120) if active else (210, 220, 235), scale=0.34, thick=1)
    add_ui_hitbox("palm_sim_toggle", btn_x, btn_y, btn_x + btn_w, btn_y + btn_h)

    # 2 camera side-by-side, khung gon hon de dat duoi web khong bi che
    margin = 10
    gap = 10
    cam_y = 50
    cam_h = WINDOW_H - cam_y - 10
    cam_w = (WINDOW_W - margin * 2 - gap) // 2

    def draw_camera_box(src_frame, x, y, w, h, title, hand_ok, badge_text, active_control):
        border_col = (51, 255, 153) if active_control else ((31, 214, 255) if hand_ok else (90, 100, 120))
        draw_panel(display, x, y, w, h, title, border=border_col)

        if src_frame is None:
            no_frame = np.zeros((FRAME_H, FRAME_W, 3), dtype=np.uint8)
            no_frame[:] = (20, 20, 25)
            put_line(no_frame, "NO CAMERA SIGNAL", 170, 230, (0, 0, 255), scale=0.7, thick=2)
            show_frame = no_frame
        else:
            show_frame = src_frame.copy()

        view = fit_to_box(show_frame, w - 4, h - 34)
        px = x + 2 + max(0, (w - 4 - view.shape[1]) // 2)
        py = y + 32 + max(0, (h - 34 - view.shape[0]) // 2)
        display[py:py + view.shape[0], px:px + view.shape[1]] = view

        bx = x + 108
        bx = draw_badge(display, bx, y + 5, "HAND OK" if hand_ok else "NO HAND", (30, 90, 60) if hand_ok else (65, 50, 40))
        bx = draw_badge(display, bx, y + 5, "CONTROL" if active_control else "STANDBY", (35, 85, 55) if active_control else (45, 55, 75))
        draw_badge(display, bx, y + 5, badge_text, (20, 65, 95))

    draw_camera_box(frame_main, margin, cam_y, cam_w, cam_h, "MAIN CAMERA", main_detected, "YOLO", active_cam == "MAIN")
    draw_camera_box(frame_phone, margin + cam_w + gap, cam_y, cam_w, cam_h, "PHONE CAMERA", phone_detected, "VIEW 2", active_cam == "PHONE")

    return display


# ===================== LANDMARK SMOOTHING =====================
class LandmarkSmoother:
    def __init__(self, world=False):
        self.world = world
        self.prev = None
        self.lost_count = 0

    def reset(self):
        self.prev = None
        self.lost_count = 0

    def mark_lost(self):
        self.lost_count += 1
        if self.lost_count >= LANDMARK_RESET_LOST_FRAMES:
            self.reset()

    def smooth_points(self, raw_points):
        if not LANDMARK_SMOOTH_ENABLE:
            return raw_points

        if self.prev is None or len(self.prev) != len(raw_points):
            self.prev = [(float(x), float(y), float(z)) for x, y, z in raw_points]
            self.lost_count = 0
            return list(self.prev)

        out = []
        for (x, y, z), (px, py, pz) in zip(raw_points, self.prev):
            dx = x - px
            dy = y - py
            dz = z - pz
            dist = math.sqrt(dx * dx + dy * dy + dz * dz)

            if dist <= LANDMARK_DIST_SLOW:
                alpha = LANDMARK_ALPHA_SLOW
            elif dist >= LANDMARK_DIST_FAST:
                alpha = LANDMARK_ALPHA_FAST
            else:
                t = (dist - LANDMARK_DIST_SLOW) / max(LANDMARK_DIST_FAST - LANDMARK_DIST_SLOW, 1e-9)
                alpha = LANDMARK_ALPHA_SLOW + (LANDMARK_ALPHA_FAST - LANDMARK_ALPHA_SLOW) * t

            out.append((px + alpha * dx, py + alpha * dy, pz + alpha * dz))

        self.prev = out
        self.lost_count = 0
        return out

    def smooth_landmark_list(self, lm_list):
        raw_points = [(lm.x, lm.y, lm.z) for lm in lm_list.landmark]
        pts = self.smooth_points(raw_points)

        out = landmark_pb2.LandmarkList() if self.world else landmark_pb2.NormalizedLandmarkList()
        for x, y, z in pts:
            lm = out.landmark.add()
            lm.x = float(x)
            lm.y = float(y)
            lm.z = float(z)
        return out


class CameraPacketFilter:
    """Independent angle-domain filter for exactly one physical camera."""

    GROUPS = {
        "thumb": ("thumb1", "thumb2", "thumb3"),
        "index": ("index1", "index2", "index3"),
        "middle": ("middle1", "middle2", "middle3"),
        "ring": ("ring1", "ring2", "ring3"),
        "pinky": ("pinky1", "pinky2", "pinky3"),
    }

    def __init__(self, name):
        self.name = str(name)
        self.prev = empty_packet()
        self.hist = {
            k: deque([0.0] * CAM_FILTER_MEDIAN_WIN, maxlen=CAM_FILTER_MEDIAN_WIN)
            for k in self.prev
        }
        self.joint_latch = {k: True for k in self.prev}
        self.finger_latch = {name: True for name in self.GROUPS}
        self.open_latch = {name: True for name in self.GROUPS}
        self.unlock_count = {name: 0 for name in self.GROUPS}
        self.relock_count = {name: 0 for name in self.GROUPS}
        self.lost_frames = 0

    def reset(self):
        self.prev = empty_packet()
        for k in self.hist:
            self.hist[k].clear()
            self.hist[k].extend([0.0] * CAM_FILTER_MEDIAN_WIN)
        self.joint_latch = {k: True for k in self.prev}
        self.finger_latch = {name: True for name in self.GROUPS}
        self.open_latch = {name: True for name in self.GROUPS}
        self.unlock_count = {name: 0 for name in self.GROUPS}
        self.relock_count = {name: 0 for name in self.GROUPS}
        self.lost_frames = 0

    def mark_lost(self):
        self.lost_frames += 1
        if self.lost_frames >= CAM_FILTER_LOST_RESET_FRAMES:
            self.reset()

    def _apply_open_lock(self, packet):
        out = dict(packet)

        for name, keys in self.GROUPS.items():
            level = max(out[k] for k in keys)

            if self.open_latch[name]:
                if level >= CAM_OPEN_UNLOCK_PCT[name]:
                    self.unlock_count[name] += 1
                else:
                    self.unlock_count[name] = 0

                if self.unlock_count[name] >= CAM_OPEN_UNLOCK_FRAMES:
                    self.open_latch[name] = False
                    self.unlock_count[name] = 0
                else:
                    for k in keys:
                        out[k] = 0.0
            else:
                if level <= CAM_OPEN_RELOCK_PCT[name]:
                    self.relock_count[name] += 1
                else:
                    self.relock_count[name] = 0

                if self.relock_count[name] >= CAM_OPEN_RELOCK_FRAMES:
                    self.open_latch[name] = True
                    self.relock_count[name] = 0
                    for k in keys:
                        out[k] = 0.0

        return out

    def update(self, raw_packet, dt):
        if raw_packet is None:
            self.mark_lost()
            return None

        self.lost_frames = 0

        median_packet = {}
        for k, v in raw_packet.items():
            self.hist[k].append(float(v))
            median_packet[k] = median_list(self.hist[k])

        filtered = apply_joint_hysteresis(median_packet, self.joint_latch)
        filtered = apply_finger_hysteresis(filtered, self.finger_latch)
        filtered = self._apply_open_lock(filtered)
        filtered = rate_limit_packet(self.prev, filtered, dt)
        filtered = smooth_packet_adaptive(self.prev, filtered)
        filtered = snap_packet_small_to_zero(filtered)

        self.prev = packet_copy(filtered)
        return packet_copy(filtered)


class DualCameraPacketSelector:
    """Stable two-camera ownership with confirmed switching and cross-fade."""

    def __init__(self, preferred_cam="PHONE"):
        preferred = str(preferred_cam).upper().strip()
        self.active_cam = preferred if preferred in ("MAIN", "PHONE") else "PHONE"
        self.switch_candidate = None
        self.switch_count = 0
        self.last_output = None
        self.last_output_time = 0.0
        self.blend_from = None
        self.blend_start = 0.0

    @staticmethod
    def _blend(a, b, t):
        if a is None:
            return packet_copy(b)
        t = clamp(float(t), 0.0, 1.0)
        return {k: lerp(float(a[k]), float(b[k]), t) for k in b}

    def reset(self):
        self.switch_candidate = None
        self.switch_count = 0
        self.last_output = None
        self.last_output_time = 0.0
        self.blend_from = None
        self.blend_start = 0.0

    def select(self, main_packet, main_detected, phone_packet, phone_detected, now_ts):
        available = {
            "MAIN": bool(main_detected and main_packet is not None),
            "PHONE": bool(phone_detected and phone_packet is not None),
        }
        packets = {"MAIN": main_packet, "PHONE": phone_packet}

        # Do not switch merely because both cameras see the hand.  Keep the
        # current viewpoint until it is genuinely unavailable.
        if available[self.active_cam]:
            self.switch_candidate = None
            self.switch_count = 0
        else:
            other = "PHONE" if self.active_cam == "MAIN" else "MAIN"
            if available[other]:
                if self.switch_candidate == other:
                    self.switch_count += 1
                else:
                    self.switch_candidate = other
                    self.switch_count = 1

                # At startup there is no old viewpoint to protect, so use the
                # only available camera immediately.  Confirmation is needed
                # only for a real runtime handover.
                if self.last_output is None:
                    self.active_cam = other
                    self.switch_candidate = None
                    self.switch_count = 0
                elif self.switch_count >= DUAL_CAM_SWITCH_CONFIRM_FRAMES:
                    self.blend_from = packet_copy(self.last_output) if self.last_output is not None else None
                    self.blend_start = float(now_ts)
                    self.active_cam = other
                    self.switch_candidate = None
                    self.switch_count = 0
            else:
                self.switch_candidate = None
                self.switch_count = 0

        if available[self.active_cam]:
            target = packets[self.active_cam]
            if self.blend_from is not None:
                blend_t = (float(now_ts) - self.blend_start) / max(DUAL_CAM_BLEND_SEC, 1e-6)
                output = self._blend(self.blend_from, target, blend_t)
                if blend_t >= 1.0:
                    self.blend_from = None
            else:
                output = packet_copy(target)

            self.last_output = packet_copy(output)
            self.last_output_time = float(now_ts)
            return output, self.active_cam, True

        # During the short switch-confirmation window, hold the last filtered
        # packet instead of instantly injecting the other camera's viewpoint.
        if (
            self.last_output is not None
            and (float(now_ts) - self.last_output_time) <= DUAL_CAM_PACKET_HOLD_SEC
            and (available["MAIN"] or available["PHONE"])
        ):
            return packet_copy(self.last_output), self.active_cam, True

        return None, self.active_cam, False

# ===================== CAMERA HELPERS =====================
def normalize_camera_source(src):
    if isinstance(src, int):
        return src
    if isinstance(src, str):
        s = src.strip()
        if s.isdigit():
            return int(s)
        return s
    return src


def open_camera_source(src, name="CAM"):
    src = normalize_camera_source(src)
    if isinstance(src, int):
        cap_obj = cv2.VideoCapture(src, cv2.CAP_DSHOW)
    else:
        cap_obj = cv2.VideoCapture(src)

    cap_obj.set(cv2.CAP_PROP_FRAME_WIDTH, FRAME_W)
    cap_obj.set(cv2.CAP_PROP_FRAME_HEIGHT, FRAME_H)
    cap_obj.set(cv2.CAP_PROP_FPS, 30)
    try:
        cap_obj.set(cv2.CAP_PROP_BUFFERSIZE, CAP_BUFFER_SIZE)
    except Exception:
        pass

    if not cap_obj.isOpened():
        print(f"Khong mo duoc {name}: {src}")
        try:
            cap_obj.release()
        except Exception:
            pass
        return None

    print(f"Da mo {name}: {src}")
    return cap_obj


def process_camera_frame(frame, hands_obj, cam_label, smoother2d=None, smoother3d=None, run_hands=True):
    if frame is None:
        return None, None, False

    try:
        if frame.shape[1] != FRAME_W or frame.shape[0] != FRAME_H:
            frame = cv2.resize(frame, (FRAME_W, FRAME_H), interpolation=cv2.INTER_AREA)
    except Exception:
        pass

    frame = cv2.flip(frame, 1)

    if not run_hands:
        return frame, None, False

    rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
    results = hands_obj.process(rgb)

    raw_packet = None
    hand_detected = False

    if results.multi_hand_landmarks:
        hand2d_raw = results.multi_hand_landmarks[0]

        hand2d = smoother2d.smooth_landmark_list(hand2d_raw) if smoother2d is not None else hand2d_raw

        if results.multi_hand_world_landmarks:
            hand3d_raw = results.multi_hand_world_landmarks[0]
            hand3d = smoother3d.smooth_landmark_list(hand3d_raw) if smoother3d is not None else hand3d_raw
        else:
            hand3d = hand2d

        mp_draw.draw_landmarks(frame, hand2d, mp_hands.HAND_CONNECTIONS)
        raw_packet = compute_joint_packet(hand3d)
        hand_detected = True

        lm = hand2d.landmark
        h, w, _ = frame.shape
        names = {"Thumb": 4, "Index": 8, "Middle": 12, "Ring": 16, "Pinky": 20}
        for name, idx in names.items():
            x = int(lm[idx].x * w)
            y = int(lm[idx].y * h)
            cv2.putText(frame, name, (x + 5, y - 5), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (0, 255, 255), 2)

    if not hand_detected:
        if smoother2d is not None:
            smoother2d.mark_lost()
        if smoother3d is not None:
            smoother3d.mark_lost()

    return frame, raw_packet, hand_detected


dual_cam_selector = None


def choose_active_packet(main_packet, main_detected, phone_packet, phone_detected, last_active_cam, now_ts=None):
    global dual_cam_selector

    mode = CAM_SELECT_MODE.upper().strip()

    if mode == "MAIN":
        return main_packet, "MAIN", main_detected

    if mode == "PHONE":
        if phone_packet is not None:
            return phone_packet, "PHONE", phone_detected
        return main_packet, "MAIN", main_detected

    # AUTO mode: both cameras still detect every frame, but control ownership
    # is sticky and only changes after consecutive misses.  This prevents the
    # old PHONE -> MAIN -> PHONE angle jump.
    if now_ts is None:
        now_ts = time.time()
    if dual_cam_selector is None:
        dual_cam_selector = DualCameraPacketSelector(CAM_AUTO_PREFER)
    return dual_cam_selector.select(
        main_packet,
        main_detected,
        phone_packet,
        phone_detected,
        now_ts,
    )

# ===================== SERIAL RUNTIME =====================
ser = None
serial_rx_buffer = ""
fsr_raw = {name: -1.0 for name in FSR_NAMES}
fsr_pct = {name: -1.0 for name in FSR_NAMES}
fsr_force_n = {name: -1.0 for name in FSR_NAMES}
fsr_hit_until = {name: 0.0 for name in FSR_NAMES}
last_fsr_rx_time = 0.0
last_serial_note = ""

# Independent palm key from ESP32, not from THUMB FSR.
palm_key_raw = 0.0
palm_key_contact = False
last_palm_key_rx_time = 0.0

# Loadcell feedback
loadcell_kg = 0.0
loadcell_n = 0.0
loadcell_raw = 0.0
last_loadcell_rx_time = 0.0
last_force_thr_send_time = 0.0
last_force_thr_sent = {name: None for name in FSR_NAMES}
last_direct_follow_force_disable_time = 0.0


def set_manual_loadcell_g(grams):
    global manual_loadcell_g, manual_loadcell_override
    global loadcell_kg, loadcell_n, loadcell_raw, last_loadcell_rx_time, last_serial_note

    grams = clamp(float(grams), 0.0, LOADCELL_MAX_KG * 1000.0)

    manual_loadcell_g = grams
    manual_loadcell_override = True

    loadcell_kg = manual_loadcell_g / 1000.0
    loadcell_n = loadcell_kg * 9.81
    loadcell_raw = -999999
    last_loadcell_rx_time = time.time()

    last_serial_note = f"MANUAL LOAD {manual_loadcell_g:.0f}g"
    print(f"[MANUAL LOAD] {manual_loadcell_g:.1f} g | {loadcell_kg:.3f} kg | {loadcell_n:.2f} N")


def set_auto_object_mass(label, grams, mu=0.55, safety_factor=2.0):
    """
    Tu dong set khoi luong khi camera nhan duoc YOLO 3 objects.
    Van dung chung bien manual_loadcell_g de giu nguyen logic tinh luc hien co.
    Neu nguoi dung nhap gram thu cong sau do, gia tri thu cong van ghi de binh thuong.
    """
    global manual_loadcell_g, manual_loadcell_override
    global loadcell_kg, loadcell_n, loadcell_raw, last_loadcell_rx_time
    global GRIP_FRICTION_COEFF, GRIP_SAFETY_FACTOR, last_serial_note

    grams = clamp(float(grams), 0.0, LOADCELL_MAX_KG * 1000.0)

    manual_loadcell_g = grams
    manual_loadcell_override = True

    GRIP_FRICTION_COEFF = float(mu)
    GRIP_SAFETY_FACTOR = float(safety_factor)

    loadcell_kg = manual_loadcell_g / 1000.0
    loadcell_n = loadcell_kg * 9.81
    loadcell_raw = -888888
    last_loadcell_rx_time = time.time()

    last_serial_note = f"AUTO OBJECT {label.upper()} {manual_loadcell_g:.0f}g"
    print(f"[AUTO OBJECT] {label} | {manual_loadcell_g:.1f} g | {loadcell_kg:.3f} kg | {loadcell_n:.2f} N | mu={GRIP_FRICTION_COEFF:.2f} | sf={GRIP_SAFETY_FACTOR:.2f}")


def apply_manual_loadcell_if_enabled(now_ts):
    global loadcell_kg, loadcell_n, loadcell_raw, last_loadcell_rx_time

    if not MANUAL_LOADCELL_ENABLE:
        return

    if not manual_loadcell_override:
        return

    loadcell_kg = clamp(manual_loadcell_g / 1000.0, 0.0, LOADCELL_MAX_KG)
    loadcell_n = loadcell_kg * 9.81
    loadcell_raw = -999999

    # Khi dung manual, coi nhu loadcell luon fresh de logic grip force van chay.
    last_loadcell_rx_time = now_ts


def commit_manual_load_input():
    global manual_load_input_text, manual_load_input_active

    try:
        txt = manual_load_input_text.strip()
        if txt == "":
            manual_load_input_active = False
            return

        grams = float(txt)
        set_manual_loadcell_g(grams)

    except Exception:
        print("[MANUAL LOAD] Gia tri nhap khong hop le.")

    manual_load_input_active = False
    manual_load_input_text = ""


grip_force_control_active = False
grip_contact_detected = False
grip_close_level_latched = False
grip_target_total_n = 0.0
grip_target_sensor_n = {name: 0.0 for name in FSR_NAMES}


def serial_send_line(line):
    global ser
    if ser is None:
        return
    try:
        ser.write((line.strip() + "\n").encode("utf-8"))
    except Exception as e:
        print(f"Serial send fail: {e}")
        try:
            ser.close()
        except Exception:
            pass
        ser = None


def parse_serial_feedback_line(line):
    global last_fsr_rx_time, last_serial_note
    global loadcell_kg, loadcell_n, loadcell_raw, last_loadcell_rx_time
    global palm_key_raw, palm_key_contact, last_palm_key_rx_time
    if not line:
        return

    up = line.strip().upper()
    if not up:
        return

    if up.startswith("FSR_RAW"):
        vals = parse_named_values(up[len("FSR_RAW"):])
        for name in FSR_NAMES:
            if name in vals:
                fsr_raw[name] = vals[name]
        last_fsr_rx_time = time.time()
        return

    if up.startswith("FSR_PCT"):
        vals = parse_named_values(up[len("FSR_PCT"):])
        for name in FSR_NAMES:
            if name in vals:
                fsr_pct[name] = vals[name]
        last_fsr_rx_time = time.time()
        return

    if up.startswith("FSR_N"):
        vals = parse_named_values(up[len("FSR_N"):])
        for name in FSR_NAMES:
            if name in vals:
                fsr_force_n[name] = vals[name]
        last_fsr_rx_time = time.time()
        return

    # V55: independent palm key/sensor, separated from THUMB force sensor.
    # Expected ESP32 line examples:
    #   PALM_KEY CONTACT=1 RAW=1234
    #   PALM CONTACT=0 RAW=0
    if up.startswith("PALM_KEY") or up.startswith("PALM "):
        key_name = "PALM_KEY" if up.startswith("PALM_KEY") else "PALM"
        vals = parse_named_values(up[len(key_name):])
        contact_val = vals.get("CONTACT", vals.get("KEY", vals.get("ON", vals.get("STATE", 0.0))))
        palm_key_contact = bool(float(contact_val) >= 0.5)
        palm_key_raw = float(vals.get("RAW", vals.get("ADC", vals.get("VALUE", 0.0))))
        last_palm_key_rx_time = time.time()
        return

    if up.startswith("FORCE_HIT"):
        parts = up.split()
        if len(parts) >= 2 and parts[1] in fsr_hit_until:
            fsr_hit_until[parts[1]] = time.time() + FSR_HIT_HOLD_SEC
        last_serial_note = up
        return

    if up.startswith("LOADCELL"):
        vals = parse_named_values(up[len("LOADCELL"):])
        if "KG" in vals:
            loadcell_kg = vals["KG"]
        if "N" in vals:
            loadcell_n = vals["N"]
        if "RAW" in vals:
            loadcell_raw = vals["RAW"]
        last_loadcell_rx_time = time.time()
        return

    if up.startswith("OK") or up.startswith("ERR") or up.startswith("ALL5_READY") or up.startswith("HX711_READY"):
        last_serial_note = up
        return


def read_serial_feedback_nonblocking():
    global ser, serial_rx_buffer
    if ser is None:
        return
    try:
        n = ser.in_waiting
        if n <= 0:
            return
        data = ser.read(n)
        if not data:
            return
        serial_rx_buffer += data.decode("utf-8", errors="ignore").replace("\r", "\n")
        while "\n" in serial_rx_buffer:
            line, serial_rx_buffer = serial_rx_buffer.split("\n", 1)
            parse_serial_feedback_line(line.strip())
    except Exception as e:
        print(f"Serial read fail: {e}")
        try:
            ser.close()
        except Exception:
            pass
        ser = None



# ===================== WEB RESET BUTTON INJECTION =====================
def install_web_reset_button_for_page2():
    """
    Them nut RESET vao khung Object/None tren Web Dashboard ma khong can sua file web rieng.

    Cong dung:
    - Xoa object latch tren Web.
    - Gui reset_to_tracking ve Python main.
    - Web nhay ve Page 1.
    - Python quay lai FREE_TRACKING_AUTO, chi detect ban tay.
    """
    global get_web_control_snapshot

    if _datn_web_module is None:
        return

    try:
        # Them control field neu chua co.
        if hasattr(_datn_web_module, "_CONTROL") and hasattr(_datn_web_module, "_CONTROL_LOCK"):
            with _datn_web_module._CONTROL_LOCK:
                _datn_web_module._CONTROL.setdefault("reset_to_tracking_seq", 0)

        # Boc update_web_control de xu ly reset_to_tracking.
        if not getattr(_datn_web_module, "_datn_reset_button_patch_installed", False):
            _orig_update_web_control = _datn_web_module.update_web_control

            def _update_web_control_with_reset(**kwargs):
                reset_req = bool(
                    kwargs.pop("reset_to_tracking", False)
                    or kwargs.pop("reset_object_latch", False)
                    or kwargs.pop("reset_to_hand_tracking", False)
                )

                _orig_update_web_control(**kwargs)

                if reset_req:
                    with _datn_web_module._CONTROL_LOCK:
                        _datn_web_module._CONTROL["mode"] = "AUTO"
                        _datn_web_module._CONTROL["reset_to_tracking_seq"] = int(
                            _datn_web_module._CONTROL.get("reset_to_tracking_seq", 0) or 0
                        ) + 1
                        _datn_web_module._CONTROL["last_update"] = time.time()

            _datn_web_module.update_web_control = _update_web_control_with_reset
            _datn_web_module._datn_reset_button_patch_installed = True

        # Chen CSS/HTML/JS vao HTML_PAGE.
        if not hasattr(_datn_web_module, "HTML_PAGE"):
            return

        page = _datn_web_module.HTML_PAGE

        if "resetObjectToTracking()" not in page:
            reset_css = r"""
    /* ===================== RESET OBJECT LATCH BUTTON ===================== */
    .object-reset-btn{
      position:relative;
      z-index:3;
      display:inline-flex;
      align-items:center;
      justify-content:center;
      margin-top:10px;
      min-height:30px;
      padding:7px 13px;
      border-radius:11px;
      border:1px solid rgba(34,211,238,.48);
      background:rgba(34,211,238,.12);
      color:#67e8f9;
      font-size:10px;
      font-weight:1000;
      text-transform:uppercase;
      letter-spacing:.075em;
      cursor:pointer;
      box-shadow:0 0 18px rgba(34,211,238,.08);
    }
    .object-reset-btn:hover{
      background:rgba(34,211,238,.20);
      border-color:rgba(34,211,238,.72);
      color:#e0faff;
    }
    .object-reset-btn:active{
      transform:translateY(1px);
    }
"""
            page = page.replace("</style>", reset_css + "\n</style>", 1)

            # Them nut ngay duoi label trong khung Object/None.
            old_label = '<div class="object-label">Label: <span id="objectLabel">none</span></div>'
            new_label = old_label + '<button class="object-reset-btn" id="resetObjectBtn" onclick="resetObjectToTracking()" title="Clear object latch and return to hand-only tracking">RESET · BACK TO HAND TRACKING</button>'
            if old_label in page:
                page = page.replace(old_label, new_label, 1)
            else:
                # Fallback neu HTML da duoc format khac.
                page = page.replace(
                    '<span id="objectLabel">none</span>',
                    '<span id="objectLabel">none</span></div><button class="object-reset-btn" id="resetObjectBtn" onclick="resetObjectToTracking()" title="Clear object latch and return to hand-only tracking">RESET · BACK TO HAND TRACKING</button><div style="display:none">',
                    1,
                )

            reset_js = r"""
function resetObjectToTracking(){
  objectLatch={
    active:false,
    label:'none',
    name:'None',
    mass_g:0,
    required_min_n:0,
    required_max_n:0,
    target_total_n:0,
    lockedAt:0
  };

  try{
    if($('objectName'))$('objectName').textContent='None';
    if($('objectLabel'))$('objectLabel').textContent='none';
    if($('massG'))$('massG').textContent='0';
    if($('loadcellN'))$('loadcellN').textContent='0.00';
    if($('requiredMin'))$('requiredMin').textContent='0.00';
    if($('requiredMax'))$('requiredMax').textContent='0.00';
    if($('targetTotal'))$('targetTotal').textContent='0.00';
    if($('actualGripN'))$('actualGripN').textContent='0.00';
    if($('gripStateText')){
      $('gripStateText').textContent='NO OBJECT';
      setTextClass($('gripStateText'),'amber-t');
    }
  }catch(e){}

  postControl({mode:'AUTO',reset_to_tracking:true});
  selectPage('page1',true);
}
"""
            page = page.replace("function togglePalmTest(){", reset_js + "\nfunction togglePalmTest(){", 1)

            _datn_web_module.HTML_PAGE = page

        # Bao dam main loop doc snapshot moi.
        get_web_control_snapshot = _datn_web_module.get_web_control_snapshot

    except Exception as e:
        print("[WEB RESET BUTTON PATCH ERROR]", e)


# ===================== STARTUP =====================
if SERIAL_ENABLE and serial is not None:
    try:
        ser = serial.Serial(SERIAL_PORT, SERIAL_BAUD, timeout=0)
        time.sleep(1.2)
        print(f"Serial OK: {SERIAL_PORT} @ {SERIAL_BAUD}")
        if FSR_FEEDBACK_ENABLE:
            if FSR_FORCE_ON_AT_START:
                serial_send_line("FORCE_ON")
                time.sleep(0.03)
            if FSR_STREAM_ON_AT_START:
                serial_send_line("FSR_STREAM_ON")
                time.sleep(0.03)
            serial_send_line("READFSR")
            time.sleep(0.03)
            serial_send_line("FORCE_MODE_N")
            time.sleep(0.03)
        if LOADCELL_ENABLE:
            if LOADCELL_STREAM_ON_AT_START:
                serial_send_line("LOAD_STREAM_ON")
                time.sleep(0.03)
            serial_send_line("READLOAD")
            time.sleep(0.03)
    except Exception as e:
        print(f"Serial FAIL: {e}")
        ser = None

cap_main = open_camera_source(CAM_MAIN_SOURCE, "MAIN CAM")
if cap_main is None:
    raise SystemExit

cap_phone = None
if CAM_PHONE_ENABLE:
    cap_phone = open_camera_source(CAM_PHONE_SOURCE, CAM_PHONE_NAME)
    if cap_phone is None:
        print("Canh bao: khong mo duoc cam dien thoai. Code van chay voi MAIN CAM.")

print("Da mo camera. Nhan Q de thoat.")
print("DATN V46: One-touch Palm Unlock. Co vat -> mo 0%, cham palm 1 lan -> mo khoa follow tay. Q=thoat.")
print("AUTO OBJECT: MAIN CAM detect YOLO 3 objects: kimquat_sauce, red_chili, orange. Ca MAIN + PHONE van detect hand.")
print(f"UNITY OBJECT UDP: send object label to {UNITY_IP}:{UNITY_OBJECT_PORT} for PythonObjectUdpLink.cs")

# ===================== YOLO 3 OBJECT AUTO GRIP INIT =====================
# Main cam 640x480.
# 3 model YOLO rieng:
# - kimquat_sauce = 200g
# - red_chili / ot bot = 80g
# - orange = 150g
object_detector = Yolo3SeparateObjectDetector(
    roi=(40, 120, 620, 460),
    process_every_n=2,
    stable_window=7,
    stable_min_count=4,
    show_roi=True,
    require_center_in_roi=True,
)

auto_grip_sm = AutoGripStateMachine(
    close_enter=12.0,      # V5: khong con doi close_enter de unlock, chi giu de phat hien mo/nam neu can
    open_enter=28.0,
    hand_stable_frames=3,  # giam tre khi chuyen WAIT_GRIP -> VISION_CLOSING
    object_timeout_sec=2.0,
)

auto_object_mass_label = "none"
object_result = ObjectResult()

# V30 OBJECT LATCH:
# Chi can YOLO nhan vat hop le 1 lan, giu label do de:
# - Unity object tiep tuc bay vao long ban tay du vat da ra khoi camera.
# - Web Page 2 van hien vat cuoi cung, khong nhay ve none.
unity_object_latch_label = "none"
unity_object_latch_mass_g = 0.0
unity_object_latch_target_force_n = 0.0
unity_object_latch_min_force_n = 0.0
unity_object_latch_max_force_n = 0.0
unity_object_latch_hard_limit_n = 0.0
unity_object_latch_mu = 0.8
unity_object_latch_safety_factor = 2.0
unity_object_latch_time = 0.0

auto_state = None

# V11:
# Palm sensor la "chia khoa" mo khoa 1 lan.
# Khi THUMB/PALM cham 1 lan sau khi da co vat/manual mass,
# palm_key_unlocked se duoc giu True, khong can de cam bien long ban tay lien tuc.
palm_key_unlocked = False

force_hold_robot_packet = None
prev_force_hold_allowed = False
prev_auto_state_name = None

# V43: timestamp luc Palm Sensor/Palm Test mo khoa.
# Dung de tri hoan FORCE_HOLD mot khoang ngan, tranh nhay 0% -> 100%.
palm_unlock_time = 0.0

# V46: nho xung cham palm gan nhat de mo khoa kieu one-touch.
palm_recent_touch_until = 0.0
palm_unlock_source = "NONE"

# V47: ghi nho palm da unlock cho vat nao.
# Neu YOLO doi sang label vat khac, he se khoa lai va yeu cau cham Palm lai 1 lan.
palm_unlocked_object_label = "none"

# V47: sau khi doi sang vat khac, neu cam bien Palm van dang bi de tu vat cu,
# khong unlock ngay. Bat buoc Palm OFF truoc, sau do cham ON lai moi unlock.
palm_wait_fresh_touch_after_object_change = False

# V30 LOST-HAND + SMOOTH + PER-FINGER RELEASE FIX:
# Neu robot dang kep/force-hold, nguoi dung MO BAN TAY roi moi dua tay ra khoi camera,
# khong duoc quay lai full_close_packet() nua.
release_open_latch = False
release_open_latch_time = 0.0
RELEASE_OPEN_LATCH_MAX_SEC = 30.0

# V30: latch mo rieng tung ngon trong FORCE_HOLD.
# True nghia la ngon do dang duoc phep mo theo camera, cac ngon khac van giu/kep vat.
force_hold_finger_release_latch = {name: False for name in MANUAL_FINGER_ORDER}

prev_packet = empty_packet()
last_unity_sent_packet = empty_packet()
last_serial_pct = {"INDEX": -1, "MIDDLE": -1, "RING": -1, "PINKY": -1, "THUMB": -1}
servo_hist = {
    "INDEX": deque(maxlen=SERIAL_MEDIAN_WIN),
    "MIDDLE": deque(maxlen=SERIAL_MEDIAN_WIN),
    "RING": deque(maxlen=SERIAL_MEDIAN_WIN),
    "PINKY": deque(maxlen=SERIAL_MEDIAN_WIN),
    "THUMB": deque(maxlen=SERIAL_MEDIAN_WIN),
}
servo_ema = {k: -1.0 for k in last_serial_pct.keys()}
servo_dbg = {k: 0 for k in last_serial_pct.keys()}

manual_target_pct = {name: 0.0 for name in MANUAL_FINGER_ORDER}
manual_current_pct = {name: 0.0 for name in MANUAL_FINGER_ORDER}

hist = {k: deque([0.0, 0.0, 0.0], maxlen=3) for k in prev_packet.keys()}
joint_zero_latch = {k: True for k in prev_packet.keys()}
finger_zero_latch = {"thumb": True, "index": True, "middle": True, "ring": True, "pinky": True}
unity_zero_latch = {"thumb": True, "index": True, "middle": True, "ring": True, "pinky": True}

lm_smoother_main_2d = LandmarkSmoother(world=False)
lm_smoother_main_3d = LandmarkSmoother(world=True)
lm_smoother_phone_2d = LandmarkSmoother(world=False)
lm_smoother_phone_3d = LandmarkSmoother(world=True)

# V48: angle filters are intentionally separate per camera.  MAIN history is
# never mixed with PHONE history, even while AUTO control changes ownership.
camera_packet_filter_main = CameraPacketFilter("MAIN")
camera_packet_filter_phone = CameraPacketFilter("PHONE")
dual_cam_selector = DualCameraPacketSelector(CAM_AUTO_PREFER)

prev_time = time.time()
last_hand_time = prev_time
last_send_time = 0.0
last_serial_time = 0.0
active_cam = "PHONE" if CAM_SELECT_MODE.upper().strip() == "PHONE" else "MAIN"
frame_counter = 0

last_frame_phone_raw = None
last_main_packet = None
last_phone_packet = None
last_main_detect_time = 0.0
last_phone_detect_time = 0.0

if PYTHON_PREVIEW_ENABLE:
    cv2.namedWindow(WIN_NAME, cv2.WINDOW_NORMAL)
    cv2.resizeWindow(WIN_NAME, SHOW_WINDOW_W, SHOW_WINDOW_H)
    cv2.moveWindow(WIN_NAME, 1160, 120)
    cv2.setMouseCallback(WIN_NAME, dashboard_mouse_event)

# Them nut RESET vao khung Object/None cua Web truoc khi start server.
install_web_reset_button_for_page2()

# ===================== START WEB DASHBOARD V7 =====================
# Dat sau khi khoi tao camera/model/serial. Khi chay code chinh,
# web se tu bat va tu mo tab tren laptop.
if WEB_DASHBOARD_ENABLE:
    if WEB_DASHBOARD_IMPORT_OK and start_web_dashboard is not None:
        try:
            start_web_dashboard(port=WEB_DASHBOARD_PORT, open_browser=WEB_DASHBOARD_OPEN_BROWSER)
            web_dashboard_ok = True
        except Exception as e:
            web_dashboard_ok = False
            print("[WEB DASHBOARD ERROR]", e)
    else:
        web_dashboard_ok = False
        print("[WEB DASHBOARD IMPORT ERROR]", WEB_DASHBOARD_IMPORT_ERROR)

# ===================== MAIN LOOP =====================
while True:
    frame_counter += 1

    ret_main, frame_main_raw = cap_main.read()
    if not ret_main:
        print("Khong doc duoc frame tu MAIN CAM")
        break

    # ===================== MAIN CAM OBJECT DETECTION =====================
    # Theo yeu cau thay: MAIN CAM van detect hand nhu cu.
    # Phan nay chi copy/resize/flip frame de detect YOLO 3 objects, khong dung MediaPipe.
    # Detect moi 3 frame trong object_detector de giam lag.
    try:
        frame_main_object = frame_main_raw
        if frame_main_object.shape[1] != FRAME_W or frame_main_object.shape[0] != FRAME_H:
            frame_main_object = cv2.resize(frame_main_object, (FRAME_W, FRAME_H), interpolation=cv2.INTER_AREA)
        frame_main_object = cv2.flip(frame_main_object, 1)
        object_result = object_detector.update(frame_main_object)

        # Khi vua bam RESET tren web, tam thoi bo qua object stable label trong
        # mot khoang ngan de web/page quay lai che do chi tracking ban tay.
        if time.time() < web_reset_ignore_object_until:
            object_result = ObjectResult()

        if object_result.stable_label in VALID_OBJECT_LABELS:
            if auto_object_mass_label != object_result.stable_label:
                set_auto_object_mass(
                    object_result.stable_label,
                    object_result.mass_g,
                    object_result.mu,
                    object_result.safety_factor,
                )
                auto_object_mass_label = object_result.stable_label
    except Exception as e:
        print("[OBJECT DETECT ERROR]", e)
        object_result = ObjectResult()

    # V30 OBJECT LATCH:
    # Khi YOLO thay vat hop le 1 lan, giu label do.
    # Neu vat ra khoi khung hinh khi Unity object dang bay, Python VAN tiep tuc gui label cu,
    # khong gui none nua, tranh object dung giua duong va mode bi ve None.
    try:
        if object_result is not None and object_result.stable_label in VALID_OBJECT_LABELS:
            detected_object_label = normalize_object_label_for_unity(object_result.stable_label)

            # ===================== V47 OBJECT CHANGE RE-LOCK =====================
            # Yeu cau moi:
            # - Cam -> cham Palm 1 lan -> unlock.
            # - Neu sau do YOLO nhan sang vat KHAC, vi du red_chili, phai KHOA LAI.
            # - Muon robot follow/nắm lai vat moi thi phai cham Palm 1 lan nua.
            object_changed_relock = (
                detected_object_label != "none"
                and unity_object_latch_label != "none"
                and detected_object_label != unity_object_latch_label
            )

            if object_changed_relock:
                palm_key_unlocked = False
                palm_unlock_time = 0.0
                palm_recent_touch_until = 0.0
                palm_unlock_source = "OBJECT_CHANGED"
                palm_unlocked_object_label = "none"
                palm_wait_fresh_touch_after_object_change = True
                palm_sim_enabled = False

                grip_force_control_active = False
                grip_contact_detected = False
                grip_close_level_latched = False
                grip_target_total_n = 0.0
                grip_target_sensor_n = {name: 0.0 for name in FSR_NAMES}

                release_open_latch = False
                release_open_latch_time = 0.0
                force_hold_robot_packet = None
                prev_force_hold_allowed = False
                prev_auto_state_name = None

                for _fname in force_hold_finger_release_latch:
                    force_hold_finger_release_latch[_fname] = False

                # Reset state machine de vat moi quay lai OBJECT_LOCKED / WAIT_PALM,
                # robot se mo 0% cho den khi cham Palm lan tiep theo.
                auto_grip_sm = AutoGripStateMachine(
                    close_enter=12.0,
                    open_enter=28.0,
                    hand_stable_frames=3,
                    object_timeout_sec=2.0,
                )
                auto_state = None

                # Ep output/filter ve mo 0 de tranh keo theo lenh nam cua vat cu.
                robot_packet = empty_packet()
                prev_packet = packet_copy(robot_packet)
                reset_hist_to_packet(hist, robot_packet)

                last_serial_note = f"OBJECT CHANGED {unity_object_latch_label}->{detected_object_label}: PALM LOCKED"
                print("[V47 OBJECT CHANGE]", unity_object_latch_label, "->", detected_object_label, "| PALM LOCKED")

            unity_object_latch_label = detected_object_label
            unity_object_latch_mass_g = float(object_result.mass_g or 0.0)
            unity_object_latch_target_force_n = float(object_result.target_force_n or 0.0)
            unity_object_latch_min_force_n = float(object_result.min_force_n or 0.0)
            unity_object_latch_max_force_n = float(object_result.max_force_n or 0.0)
            unity_object_latch_hard_limit_n = float(object_result.hard_limit_n or 0.0)
            unity_object_latch_mu = float(getattr(object_result, "mu", unity_object_latch_mu))
            unity_object_latch_safety_factor = float(getattr(object_result, "safety_factor", unity_object_latch_safety_factor))
            unity_object_latch_time = time.time()

        send_object_to_unity(unity_object_latch_label, time.time())
    except Exception as e:
        print("[UNITY OBJECT SEND ERROR]", e)

    frame_phone_raw = None
    if cap_phone is not None:
        if last_frame_phone_raw is None or (frame_counter % PHONE_READ_EVERY_N == 0):
            ret_phone, tmp = cap_phone.read()
            if ret_phone:
                last_frame_phone_raw = tmp
        frame_phone_raw = last_frame_phone_raw

    now = time.time()
    dt = now - prev_time
    prev_time = now


    # ===================== WEB PAGE1 CONTROL POLL =====================
    # Page 1 tren web co nut AUTO/MANU va 5 slider MANU.
    # Chi ap dung khi web co lenh moi, khong tranh voi UI Python cu.
    if WEB_CONTROL_FROM_DASHBOARD_ENABLE and web_dashboard_ok and get_web_control_snapshot is not None:
        try:
            ctrl = get_web_control_snapshot()
            ctrl_ts = float(ctrl.get("last_update", 0.0) or 0.0)
            if ctrl_ts > last_web_control_apply_time:
                last_web_control_apply_time = ctrl_ts
                web_mode = str(ctrl.get("mode", CONTROL_MODE)).upper().strip()
                if web_mode in ("AUTO", "MANU"):
                    CONTROL_MODE = web_mode

                for _fname in MANUAL_FINGER_ORDER:
                    _key = "manual_" + _fname.lower() + "_pct"
                    if _key in ctrl:
                        manual_target_pct[_fname] = clamp(float(ctrl.get(_key, manual_target_pct[_fname])), 0.0, 100.0)

                # V30: nut Palm Test tren web. Khoa cheo voi cam bien that.
                seq = int(ctrl.get("palm_test_toggle_seq", 0) or 0)
                if seq != last_web_palm_toggle_seq:
                    last_web_palm_toggle_seq = seq
                    if palm_sensor_contact():
                        palm_sim_enabled = False
                        palm_key_unlocked = True
                        last_serial_note = "PALM HW ACTIVE - TEST LOCKED"
                        print("[PALM TEST WEB LOCKED] Hardware palm sensor is ON")
                    else:
                        palm_sim_enabled = not palm_sim_enabled
                        # V46: Palm Test OFF chi tat tin hieu gia lap, khong huy palm_key_unlocked
                        # neu he da duoc mo khoa one-touch sau khi co vat. Muon ve ban dau dung nut RESET.
                        if not palm_sim_enabled and not palm_key_unlocked:
                            palm_key_unlocked = False
                        last_serial_note = "PALM TEST WEB ON" if palm_sim_enabled else "PALM TEST WEB OFF"
                        print("[PALM TEST WEB]", "ON" if palm_sim_enabled else "OFF")

                # V44: Nut RESET trong khung Object/None tren Web.
                # Cong dung: xoa vat da lock, xoa palm latch, ve lai FREE_TRACKING/AUTO
                # de chi detect ban tay, khong can cham palm sensor.
                reset_seq = int(ctrl.get("reset_to_tracking_seq", 0) or 0)
                if reset_seq != last_web_reset_to_tracking_seq:
                    last_web_reset_to_tracking_seq = reset_seq

                    CONTROL_MODE = "AUTO"

                    # Xoa object latch Python + Unity.
                    unity_object_latch_label = "none"
                    unity_object_latch_mass_g = 0.0
                    unity_object_latch_target_force_n = 0.0
                    unity_object_latch_min_force_n = 0.0
                    unity_object_latch_max_force_n = 0.0
                    unity_object_latch_hard_limit_n = 0.0
                    unity_object_latch_time = 0.0
                    auto_object_mass_label = "none"
                    object_result = ObjectResult()
                    web_reset_ignore_object_until = now + WEB_RESET_OBJECT_IGNORE_SEC

                    # Xoa khoi luong/target de Page 2 ve None/0N.
                    manual_loadcell_g = 0.0
                    manual_loadcell_override = True
                    loadcell_kg = 0.0
                    loadcell_n = 0.0
                    loadcell_raw = 0.0
                    last_loadcell_rx_time = 0.0

                    # Xoa palm/force latches.
                    palm_key_unlocked = False
                    palm_unlock_time = 0.0
                    palm_recent_touch_until = 0.0
                    palm_unlock_source = "RESET"
                    palm_unlocked_object_label = "none"
                    palm_wait_fresh_touch_after_object_change = False
                    palm_sim_enabled = False
                    grip_force_control_active = False
                    grip_contact_detected = False
                    grip_close_level_latched = False
                    grip_target_total_n = 0.0
                    grip_target_sensor_n = {name: 0.0 for name in FSR_NAMES}
                    release_open_latch = False
                    release_open_latch_time = 0.0
                    force_hold_robot_packet = None
                    prev_force_hold_allowed = False
                    prev_auto_state_name = None
                    for _fname in force_hold_finger_release_latch:
                        force_hold_finger_release_latch[_fname] = False

                    # Reset state machine ve ban dau.
                    auto_grip_sm = AutoGripStateMachine(
                        close_enter=12.0,
                        open_enter=28.0,
                        hand_stable_frames=3,
                        object_timeout_sec=2.0,
                    )
                    auto_state = None

                    # Gui none sang Unity object link de huy object latch neu Unity co xu ly none.
                    send_object_to_unity("none", now_ts=now, force=True)

                    last_serial_note = "WEB RESET: HAND TRACKING ONLY"
                    print("[WEB RESET] Back to hand-only tracking / Page 1")
        except Exception:
            pass

    # Doc MANU target tu chuot tren dashboard/web; current duoc lam mem de servo chay tu tu
    manual_current_pct = update_manual_percent_smooth(manual_target_pct, manual_current_pct, dt)

    read_serial_feedback_nonblocking()
    apply_manual_loadcell_if_enabled(now)

    mode_now = CAM_SELECT_MODE.upper().strip()

    if PROCESS_BOTH_CAMS:
        run_main_hands = True
        run_phone_hands = frame_phone_raw is not None
    else:
        if mode_now == "MAIN":
            run_main_hands = True
            run_phone_hands = frame_phone_raw is not None and (frame_counter % SECONDARY_PROCESS_EVERY_N == 0)
        elif mode_now == "PHONE":
            run_main_hands = (frame_counter % SECONDARY_PROCESS_EVERY_N == 0)
            run_phone_hands = frame_phone_raw is not None
        else:
            if active_cam == "PHONE":
                run_phone_hands = frame_phone_raw is not None
                run_main_hands = (frame_counter % SECONDARY_PROCESS_EVERY_N == 0)
            else:
                run_main_hands = True
                run_phone_hands = frame_phone_raw is not None and (frame_counter % SECONDARY_PROCESS_EVERY_N == 0)

    frame_main, main_packet_new, main_detected_new = process_camera_frame(
        frame_main_raw, hands_main, "MAIN",
        lm_smoother_main_2d, lm_smoother_main_3d,
        run_hands=run_main_hands,
    )

    # Ve box/ROI YOLO 3 vat len MAIN CAM sau khi MediaPipe ve tay.
    # Do object detector da detect tren frame resize+flip giong process_camera_frame, toa do se khop.
    if frame_main is not None and object_result is not None:
        frame_main = object_detector.draw_overlay(frame_main, object_result)

    frame_phone, phone_packet_new, phone_detected_new = (None, None, False)
    if frame_phone_raw is not None:
        frame_phone, phone_packet_new, phone_detected_new = process_camera_frame(
            frame_phone_raw, hands_phone, CAM_PHONE_NAME,
            lm_smoother_phone_2d, lm_smoother_phone_3d,
            run_hands=run_phone_hands,
        )

    if run_main_hands:
        if main_detected_new and main_packet_new is not None:
            last_main_packet = camera_packet_filter_main.update(main_packet_new, dt)
            last_main_detect_time = now
        else:
            camera_packet_filter_main.mark_lost()
            if (now - last_main_detect_time) > DETECTION_HOLD_SEC:
                last_main_packet = None

    if run_phone_hands:
        if phone_detected_new and phone_packet_new is not None:
            last_phone_packet = camera_packet_filter_phone.update(phone_packet_new, dt)
            last_phone_detect_time = now
        else:
            camera_packet_filter_phone.mark_lost()
            if (now - last_phone_detect_time) > DETECTION_HOLD_SEC:
                last_phone_packet = None


    # ===================== WEB DASHBOARD CAMERA UPDATE =====================
    # V30: khong gui camera len web. Camera hien trong cua so Python thu gon.

    main_detected = (now - last_main_detect_time) <= DETECTION_HOLD_SEC
    phone_detected = (now - last_phone_detect_time) <= DETECTION_HOLD_SEC
    main_packet = last_main_packet if main_detected else None
    phone_packet = last_phone_packet if phone_detected else None

    selected_packet, active_cam, selected_hand_detected = choose_active_packet(
        main_packet, main_detected, phone_packet, phone_detected, active_cam, now_ts=now
    )

    raw_packet = None
    if selected_packet is not None:
        raw_packet = selected_packet
        if selected_hand_detected:
            last_hand_time = now

    if raw_packet is None:
        # V30: Khi khong co ban tay trong khung hinh thi thong so ve 0,
        # khong giu trang thai gan nhat nua.
        raw_packet = empty_packet()

    if CONTROL_MODE == "MANU":
        robot_packet = build_robot_packet_from_manual(manual_current_pct)
        robot_packet = snap_packet_small_to_zero(robot_packet)
        prev_packet = packet_copy(robot_packet)
        reset_hist_to_packet(hist, robot_packet)
    else:
        # V48: selected_packet has already passed the independent MAIN/PHONE
        # median, hysteresis, open lock and EMA, then the dual-camera selector.
        # Do not run it through a shared history here: that was the path that
        # mixed two viewpoints and was also reset by WAIT_PALM/FORCE states.
        robot_packet = snap_packet_small_to_zero(packet_copy(raw_packet))

    # ===================== AUTO GRIP STATE MACHINE =====================
    # Tach 2 viec:
    # 1) robot_packet_camera: goi cua camera dung lam INPUT de state machine biet nguoi dung dang nam/mo tay.
    # 2) robot_packet: goi CUOI CUNG gui sang Unity/ESP32 sau khi qua safety gate.
    robot_packet_camera = packet_copy(robot_packet)

    # Palm key doc rieng tu ESP32, THUMB FSR la luc ngon cai.
    # V11:
    # - palm_contact_raw: tin hieu that tai thoi diem hien tai.
    # - palm_key_unlocked: latch mo khoa. Chi can cham 1 lan la True.
    # - auto_state nhan palm_contact = palm_key_unlocked, KHONG bat giu cam bien long lien tuc.
    palm_contact_hw = palm_sensor_contact()

    # Khoa cheo PALM SENSOR / PALM TEST:
    # neu cam bien that dang cham, tu tat PALM TEST de khong anh huong qua trinh.
    if palm_contact_hw and palm_sim_enabled:
        palm_sim_enabled = False
        last_serial_note = "PALM HW ACTIVE - PALM TEST AUTO OFF"

    palm_contact_raw = bool(palm_contact_hw or palm_sim_enabled)

    object_or_manual_ready_for_palm_key = (
        (unity_object_latch_label in VALID_OBJECT_LABELS)
        or (
            manual_loadcell_override
            and manual_loadcell_g >= 30.0
            and unity_object_latch_label == "none"
        )
    )

    # V47: neu vua doi sang vat khac, khong duoc dung tin hieu Palm dang bi giu san de unlock.
    # Bat buoc sensor ve OFF truoc, sau do cham ON lai.
    if palm_wait_fresh_touch_after_object_change:
        palm_recent_touch_until = 0.0
        if not palm_contact_raw:
            palm_wait_fresh_touch_after_object_change = False
            print("[V47 PALM] Ready for fresh Palm touch after object change")

    # V46/V47: ghi nho xung cham palm.
    # Muc dich: chi can cham 1 cai ngan la du, khong bat buoc giu cam bien.
    if palm_contact_raw and not palm_wait_fresh_touch_after_object_change:
        palm_recent_touch_until = now + PALM_TOUCH_MEMORY_SEC

    palm_touch_recent = bool(now <= palm_recent_touch_until)

    if (
        PALM_ONE_TOUCH_UNLOCK_ENABLE
        and object_or_manual_ready_for_palm_key
        and not palm_wait_fresh_touch_after_object_change
        and (palm_contact_raw or palm_touch_recent)
    ):
        # V46/V47 ONE-TOUCH UNLOCK:
        # Sau khi da co vat/manual mass, chi can Palm ON mot lan.
        # Khi bo tay/vat ra khoi FSR long ban tay, palm_key_unlocked van giu True.
        # Neu doi sang vat khac, latch nay da bi xoa va phai cham Palm lai.
        if not palm_key_unlocked:
            palm_unlock_time = now
            palm_unlock_source = "HW" if palm_contact_hw else ("TEST" if palm_sim_enabled else "RECENT")
            palm_unlocked_object_label = unity_object_latch_label if unity_object_latch_label in VALID_OBJECT_LABELS else "manual_load"
            last_serial_note = f"PALM ONE-TOUCH UNLOCK FOR {palm_unlocked_object_label}"
            print("[PALM V47] ONE-TOUCH UNLOCK source=", palm_unlock_source, "object=", palm_unlocked_object_label)
        palm_key_unlocked = True

    palm_contact = bool(palm_key_unlocked)

    # FSR ngon tay that su cham vat: tinh du 5 ngon THUMB/INDEX/MIDDLE/RING/PINKY.
    finger_contact = any_finger_fsr_contact()

    manual_mass_active = (
        manual_loadcell_override
        and manual_loadcell_g >= 30.0
        and unity_object_latch_label == "none"
    )

    # ===================== V30 LOST-HAND + SMOOTH + PER-FINGER RELEASE FIX =====================
    # Loi cu:
    # - Khi dang FORCE_HOLD, branch safety gate ben duoi luon gui full_close_packet().
    # - Neu nguoi dung mo tay roi dua tay ra khoi camera qua nhanh,
    #   state machine co the chua kip RELEASE/reset, auto_state.force_hold_allowed van con True.
    # - Ket qua: camera mat tay -> robot lai bi ep full_close_packet -> cac ngon kep lai.
    #
    # Sua:
    # - Khi con thay tay va lenh tay da MO ro rang, neu truoc do dang/gan dang kep vat,
    #   bat release_open_latch.
    # - Neu mat tay sau khi release_open_latch=True, safety gate se giu robot_packet = empty_packet().
    # - Neu nguoi dung dua tay vao lai va nam ro rang, latch duoc xoa de cho phep kep tiep.
    release_close_level_now = robot_close_level(robot_packet_camera)

    if not object_or_manual_ready_for_palm_key:
        release_open_latch = False
    elif selected_hand_detected:
        if (
            release_close_level_now <= GRIP_CLOSE_LEVEL_EXIT
            and (
                prev_force_hold_allowed
                or grip_force_control_active
                or prev_auto_state_name in ("FORCE_HOLD", "VISION_GRIP_CLOSING_AUTO")
            )
        ):
            release_open_latch = True
            release_open_latch_time = now
        elif release_close_level_now >= GRIP_CLOSE_LEVEL_ENTER:
            release_open_latch = False
    elif release_open_latch and (now - release_open_latch_time) > RELEASE_OPEN_LATCH_MAX_SEC:
        release_open_latch = False

    # V41 SAFETY FIX:
    # Loi truoc: sau khi palm sensor unlock, neu 1 FSR ngon bi cham/nhiễu trong luc chua co ban tay trong camera,
    # AutoGripStateMachine co the vao FORCE_HOLD, va safety gate se gui full_close_packet() = 100% tat ca ngon.
    #
    # Sua: chi cho phep FSR ngon kich FORCE_HOLD khi camera dang thay ban tay va tay dang nam/dong ro.
    force_hold_entry_close_level = robot_close_level(robot_packet_camera)

    # V43: khong cho FORCE_HOLD vao qua som ngay sau khi cham Palm Sensor.
    # Neu khong co delay nay, FSR ngon cham/nhieu co the lam state nhay sang FORCE_HOLD,
    # branch ben duoi gui full_close_packet() va web/robot nhay deu len 100%.
    force_hold_armed_by_time = bool(
        (palm_unlock_time <= 0.0)
        or ((now - palm_unlock_time) >= FORCE_HOLD_ARM_DELAY_SEC)
    )

    direct_follow_candidate_for_state = bool(
        DIRECT_FOLLOW_AFTER_PALM_ENABLE
        and object_or_manual_ready_for_palm_key
        and palm_key_unlocked
        and selected_hand_detected
        and selected_packet is not None
    )

    if direct_follow_candidate_for_state:
        # V45: khi camera con thay tay sau Palm, khong cho FSR day state vao FORCE_HOLD.
        # FORCE_HOLD/full_close chi nen de sau khi khong can follow tay nua.
        finger_contact_for_state = False
    elif FORCE_HOLD_REQUIRE_HAND_CLOSE:
        finger_contact_for_state = bool(
            finger_contact
            and selected_hand_detected
            and force_hold_entry_close_level >= FORCE_HOLD_ENTRY_MIN_CLOSE_LEVEL
            and force_hold_armed_by_time
        )
    else:
        finger_contact_for_state = bool(finger_contact and force_hold_armed_by_time)

    auto_state = auto_grip_sm.update(
        object_label=unity_object_latch_label,
        object_mass_g=unity_object_latch_mass_g,
        palm_contact=palm_contact,
        any_finger_contact=finger_contact_for_state,
        hand_detected=selected_hand_detected,
        robot_packet=robot_packet_camera,
        manual_mass_active=manual_mass_active,
    )

    # ===================== V7 PALM CONTACT DIRECT FOLLOW FIX =====================
    # Loi V6:
    # - Khi dang WAIT_PALM, safety gate lien tuc ep prev_packet/hist ve 0.
    # - Khi vua chuyen sang VISION_GRIP_CLOSING_AUTO, robot_packet_camera van co the bi keo ve 0
    #   boi bo loc/rate limit, nen UI hien VISION_CLOSING nhung Robot Hand Data van 0.
    #
    # Sua V7:
    # - Trong VISION_GRIP_CLOSING_AUTO, neu camera dang thay tay thi lay truc tiep
    #   selected_packet lam goi dieu khien live.
    # - Khi vua vao VISION_CLOSING, reset hist/prev_packet theo tay hien tai de robot follow ngay.
    entered_vision_closing = (
        prev_auto_state_name != "VISION_GRIP_CLOSING_AUTO"
        and auto_state.state == "VISION_GRIP_CLOSING_AUTO"
    )

    if (
        auto_state.state == "VISION_GRIP_CLOSING_AUTO"
        and selected_hand_detected
        and selected_packet is not None
        and not DIRECT_FOLLOW_AFTER_PALM_ENABLE
    ):
        # V30 SMOOTH FOLLOW FIX:
        # Ban cu lay selected_packet truc tiep nen sau khi cham palm, thanh/UI co the giat theo landmark.
        # Ban moi van follow ngay nhung qua rate_limit + EMA nhe de thanh di muot hon.
        live_follow_raw = snap_packet_small_to_zero(packet_copy(selected_packet))

        if entered_vision_closing:
            # V43 IMPORTANT:
            # Khong reset prev_packet/hist ve live_follow_raw.
            # Logic mong muon: da nhan vat -> robot dang mo 0%; cham palm -> moi bat dau follow tay.
            # Neu prev_packet = live_follow_raw, web/robot se nhay thang tu 0% len 80-90%.
            # O day giu prev_packet hien tai, thuong la 0%, roi ramp bang rate_limit + EMA ben duoi.
            reset_hist_to_packet(hist, prev_packet)

        limited_live = rate_limit_packet(prev_packet, live_follow_raw, dt)
        live_follow_packet = smooth_packet_adaptive(prev_packet, limited_live)
        live_follow_packet = snap_packet_small_to_zero(live_follow_packet)

        robot_packet_camera = packet_copy(live_follow_packet)
        prev_packet = packet_copy(live_follow_packet)

    # ===================== SAFETY GATE OUTPUT =====================
    # FREE_TRACKING_AUTO: cho robot di theo tay de thay co xem demo cu dong.
    # OBJECT_LOCKED / WAIT_PALM / WAIT_GRIP: van detect tay, nhung robot PHAI MO/DUNG YEN.
    # VISION_CLOSING: V5 sau khi palm_contact la cho robot follow tay ngay.
    # FORCE_HOLD: dong bang vi tri hien tai, sau do dieu khien luc theo FSR.
    # RELEASE: gui lenh mo tay.
    # V30: neu da co lenh MO TAY truoc khi mat camera, uu tien GIU MO.
    # Khong cho FORCE_HOLD quay lai full_close_packet khi selected_hand_detected=False.
    release_open_hold_output = bool(
        release_open_latch
        and object_or_manual_ready_for_palm_key
        and (
            not selected_hand_detected
            or robot_close_level(robot_packet_camera) <= GRIP_CLOSE_LEVEL_EXIT
        )
    )

    # V44 DIRECT FOLLOW AFTER PALM:
    # Loi thuc te: sau khi da detect vat + cham palm, state machine co the con dang WAIT/
    # FORCE_HOLD nen robot khong bam tay ngay, gay tre vai giay khi nam/mo.
    # Sua: neu da unlock palm va camera dang thay tay, cho output bam truc tiep robot_packet_camera.
    # Truoc khi palm unlock van giu logic cu: co vat -> mo 0%.
    palm_direct_follow_output = bool(
        DIRECT_FOLLOW_AFTER_PALM_ENABLE
        and object_or_manual_ready_for_palm_key
        and palm_key_unlocked
        and selected_hand_detected
        and selected_packet is not None
    )

    if release_open_hold_output:
        robot_packet = empty_packet()
        prev_packet = packet_copy(robot_packet)
        reset_hist_to_packet(hist, robot_packet)
        force_hold_robot_packet = None
        for _fname in force_hold_finger_release_latch:
            force_hold_finger_release_latch[_fname] = False

    elif palm_direct_follow_output:
        # V48 FILTERED DIRECT FOLLOW:
        # selected_packet is no longer raw.  It has already passed the
        # camera-specific median/hysteresis/open-lock filter and the stable
        # dual-camera selector.  Only output rate limiting remains here so the
        # robot ramps safely from the WAIT_PALM 0% command.
        live_direct_raw = snap_packet_small_to_zero(packet_copy(selected_packet))
        limited_direct = rate_limit_packet(prev_packet, live_direct_raw, dt)
        robot_packet = smooth_packet_adaptive(prev_packet, limited_direct)
        robot_packet = snap_packet_small_to_zero(robot_packet)

        prev_packet = packet_copy(robot_packet)
        reset_hist_to_packet(hist, robot_packet)
        force_hold_robot_packet = None

        # V45: tat force threshold thuc te trong luc dang follow tay, tranh ESP32 force-hit latch
        # lam ngon khong mo/khong dong.
        grip_force_control_active = False
        grip_contact_detected = False
        maybe_disable_force_thresholds_for_direct_follow(now)

    elif auto_state.force_hold_allowed:
        # V41 EXTRA GUARD:
        # Neu state bi vao FORCE_HOLD bat thuong ngay luc chua co hand-close hop le,
        # khong gui full_close_packet. Truong hop nay thuong do FSR cham/nhiễu sau palm unlock.
        # Neu he da vao FORCE_HOLD hop le tu vong lap truoc, prev_force_hold_allowed=True va van giu vat.
        if (
            FORCE_HOLD_REQUIRE_HAND_CLOSE
            and not prev_force_hold_allowed
            and not (selected_hand_detected and robot_close_level(robot_packet_camera) >= FORCE_HOLD_ENTRY_MIN_CLOSE_LEVEL)
        ):
            robot_packet = empty_packet()
            prev_packet = packet_copy(robot_packet)
            reset_hist_to_packet(hist, robot_packet)
            force_hold_robot_packet = None
        else:
            # ===================== V9 PER-FINGER FORCE HOLD =====================
            # Khong freeze toan bo ban tay tai goc cham dau tien nua.
            #
            # Logic moi dung voi yeu cau:
            # - Python tiep tuc gui y dinh NAM HET HANH TRINH 100%.
            # - ESP32 da co force limit RIENG TUNG NGON trong updateServosNonBlocking().
            # - Ngon nao co FSR dat nguong FORCE_THR_N thi ESP32 tu dung ngon do tai currentCmd.
            # - Ngon nao chua co FSR tac dong thi van tiep tuc di ve close 100%.
            #
            # Nhu vay:
            # - INDEX cham -> INDEX bi force limit.
            # - MIDDLE/RING/PINKY chua cham -> van dong tiep de om vat.
            # - Neu sau do ngon nao cham thi ESP32 tu dung rieng ngon do.
            #
            # Release van doc tu robot_packet_camera rieng, nen mo tay van thoat FORCE_HOLD.
            force_hold_base_packet = full_close_packet()
            robot_packet = apply_force_hold_per_finger_release(
                force_hold_base_packet,
                robot_packet_camera,
                selected_hand_detected,
                force_hold_finger_release_latch,
            )
            force_hold_robot_packet = None
            
    elif auto_state.vision_follow_allowed:
        # FREE_TRACKING_AUTO hoac VISION_CLOSING_AUTO
        robot_packet = packet_copy(robot_packet_camera)

        # V7: khi da duoc phep follow, prev_packet phai bam theo output that.
        # Neu khong, lan sau filter co the keo ve 0 do truoc do WAIT_PALM da reset.
        prev_packet = packet_copy(robot_packet)
        if auto_state.state == "VISION_GRIP_CLOSING_AUTO":
            reset_hist_to_packet(hist, robot_packet)

        force_hold_robot_packet = None

    else:
        # OBJECT_LOCKED / WAIT_PALM / WAIT_GRIP / RELEASE:
        # khong cho robot gap theo diem tay. Gui lenh mo tay ve 0 de an toan.
        robot_packet = empty_packet()
        prev_packet = packet_copy(robot_packet)
        reset_hist_to_packet(hist, robot_packet)
        force_hold_robot_packet = None

    prev_force_hold_allowed = bool(auto_state.force_hold_allowed)
    prev_auto_state_name = auto_state.state

    # V11 reset latch:
    # Sau khi release xong state machine reset ve FREE_TRACKING thi khoa palm phai ve False
    # de lan nam vat tiep theo phai cham palm lai 1 lan.
    if auto_state.state == "FREE_TRACKING_AUTO" and not object_or_manual_ready_for_palm_key:
        palm_key_unlocked = False
        palm_unlock_time = 0.0
        palm_recent_touch_until = 0.0
        palm_unlock_source = "NONE"
        palm_unlocked_object_label = "none"
        palm_wait_fresh_touch_after_object_change = False
        for _fname in force_hold_finger_release_latch:
            force_hold_finger_release_latch[_fname] = False

    # Trong FORCE_HOLD, khong yeu cau camera phai tiep tuc thay tay de giu luc.
    hand_for_grip_logic = bool(selected_hand_detected or auto_state.force_hold_allowed)

    update_grip_force_state(now, hand_for_grip_logic, robot_packet)

    # V30/V45: khi da release latch hoac dang direct-follow sau Palm, khong cho force active ep kep.
    if release_open_hold_output:
        grip_force_control_active = False
        grip_contact_detected = False

    if palm_direct_follow_output:
        # V45: day la loi lam ban thay mo/nam bi tre: FORCE_HOLD/FSR threshold co the latch servo.
        # Khi camera con thay tay, uu tien follow tay; khong gui target force thap xuong ESP32.
        grip_force_control_active = False
        grip_contact_detected = False
        maybe_disable_force_thresholds_for_direct_follow(now)
    elif release_open_hold_output:
        grip_force_control_active = False
    elif not auto_state.force_hold_allowed:
        grip_force_control_active = False
    elif loadcell_has_valid_object(now):
        # Giu trang thai force active khi da vao FORCE_HOLD, tranh bi mat do camera jitter.
        grip_force_control_active = True
        grip_contact_detected = True

    maybe_send_loadcell_force_thresholds(now)

    unity_motion_packet = build_unity_packet(robot_packet, unity_zero_latch)
    force_dashboard_packet = build_force_dashboard_packet(now)
    initial_target_sensor_n = compute_initial_fsr_force_map(now)

    unity_full_packet = dict(unity_motion_packet)
    unity_full_packet.update(force_dashboard_packet)

    # Gui them trang thai object/auto grip sang Unity.
    # V30: dung object latch de Unity UI khong bi nhay ve None khi vat roi khoi camera.
    if unity_object_latch_label != "none":
        unity_full_packet.update({
            "object_label": unity_object_latch_label,
            "object_raw_label": unity_object_latch_label,
            "object_mass_g": round(float(unity_object_latch_mass_g), 1),
            "object_target_force_n": round(float(unity_object_latch_target_force_n), 3),
            "object_min_force_n": round(float(unity_object_latch_min_force_n), 3),
            "object_max_force_n": round(float(unity_object_latch_max_force_n), 3),
            "object_hard_limit_n": round(float(unity_object_latch_hard_limit_n), 3),
        })

    if auto_state is not None:
        unity_full_packet.update({
            "auto_grip_state": auto_state.state,
            "auto_grip_status": auto_state.status,
            "force_hold_allowed": bool(auto_state.force_hold_allowed),
            "palm_key_unlocked": bool(palm_key_unlocked),
            "palm_contact_raw": bool(palm_contact_raw),
            "palm_contact_hw": bool(palm_contact_hw),
            "palm_sim_enabled": bool(palm_sim_enabled),
            "palm_one_touch_unlocked": bool(palm_key_unlocked),
            "palm_unlock_source": str(palm_unlock_source),
            "palm_unlocked_object_label": str(palm_unlocked_object_label),
            "palm_wait_fresh_touch_after_object_change": bool(palm_wait_fresh_touch_after_object_change),
            "release_open_latch": bool(release_open_latch),
            "release_open_hold_output": bool(release_open_hold_output),
            "palm_direct_follow_output": bool(palm_direct_follow_output),
            "finger_release_latch": ",".join([k for k, v in force_hold_finger_release_latch.items() if v]),
        })


    # ===================== WEB DASHBOARD DATA UPDATE =====================
    # Gui thong so THAT tu code Python chinh len web V7.
    # Giu nguyen dashboard OpenCV trong Python, chi them web song song.
    if web_dashboard_ok and update_dashboard is not None and (now - last_web_update) >= WEB_DASHBOARD_DATA_INTERVAL:
        last_web_update = now

        try:
            obj_label_web = "none"
            obj_name_web = "None"
            obj_mass_g_web = 0.0

            if unity_object_latch_label != "none":
                obj_label_web = normalize_object_label_for_unity(unity_object_latch_label)
                obj_mass_g_web = float(unity_object_latch_mass_g or 0.0)

            if obj_label_web == "kimquat_sauce":
                obj_name_web = "Kimquat Sauce"
                if obj_mass_g_web <= 0.0:
                    obj_mass_g_web = 200.0
            elif obj_label_web == "red_chili":
                obj_name_web = "Red Chili"
                if obj_mass_g_web <= 0.0:
                    obj_mass_g_web = 80.0
            elif obj_label_web == "orange":
                obj_name_web = "Orange"
                if obj_mass_g_web <= 0.0:
                    obj_mass_g_web = 200.0
            else:
                obj_label_web = "none"
                obj_name_web = "None"
                if manual_loadcell_override and manual_loadcell_g >= 30.0:
                    obj_name_web = "Manual Load"
                    obj_mass_g_web = float(manual_loadcell_g)
                else:
                    obj_mass_g_web = 0.0

            if manual_loadcell_override and manual_loadcell_g >= 30.0 and obj_label_web == "none":
                obj_mass_g_web = float(manual_loadcell_g)

            if auto_state is not None:
                grip_state_web = str(auto_state.state)
                auto_state_web = str(auto_state.status)
            else:
                grip_state_web = "UNKNOWN"
                auto_state_web = "INIT"

            esp32_web = "Connected" if ser is not None else ("Disabled" if not SERIAL_ENABLE else "Disconnected")
            unity_web = f"UDP {UNITY_PORT}/{UNITY_OBJECT_PORT} OK"

            camera_status_web = f"MAIN:{'OK' if main_detected else 'NO'} PHONE:{'OK' if phone_detected else 'NO'}"
            yolo_status_web = "Ready"
            if obj_label_web != "none":
                yolo_status_web = f"Detect {obj_label_web}"

            update_dashboard(
                object_label=obj_label_web,
                object_name=obj_name_web,
                mass_g=obj_mass_g_web,
                grip_state=grip_state_web,
                palm_sensor=bool(palm_contact_hw or (WEB_SHOW_PALM_UNLOCK_AS_SENSOR_ON and palm_key_unlocked)),
                palm_test=bool(palm_sim_enabled),
                loadcell_n=float(loadcell_n),
                required_min_n=float(force_dashboard_packet.get("required_min_n", 0.0)),
                required_max_n=float(force_dashboard_packet.get("required_max_n", 0.0)),
                target_total_n=float(force_dashboard_packet.get("recommended_n", 0.0)),
                fps=float(1.0 / max(dt, 1e-6)),
                esp32_status=esp32_web,
                unity_status=unity_web,
                serial_note=str(last_serial_note) if last_serial_note else "",
                mode=str(CONTROL_MODE),
                auto_state=auto_state_web,
                camera_status=camera_status_web,
                yolo_status=yolo_status_web,
                hand_status=f"ACTIVE {active_cam}",
                fsr_thumb_n=float(safe_fsr_n("THUMB")),
                fsr_index_n=float(safe_fsr_n("INDEX")),
                fsr_middle_n=float(safe_fsr_n("MIDDLE")),
                fsr_ring_n=float(safe_fsr_n("RING")),
                fsr_pinky_n=float(safe_fsr_n("PINKY")),
                target_thumb_n=float(initial_target_sensor_n.get("THUMB", 0.0)),
                target_index_n=float(initial_target_sensor_n.get("INDEX", 0.0)),
                target_middle_n=float(initial_target_sensor_n.get("MIDDLE", 0.0)),
                target_ring_n=float(initial_target_sensor_n.get("RING", 0.0)),
                target_pinky_n=float(initial_target_sensor_n.get("PINKY", 0.0)),
                touch_target_thumb_n=float(grip_target_sensor_n.get("THUMB", 0.0)),
                touch_target_index_n=float(grip_target_sensor_n.get("INDEX", 0.0)),
                touch_target_middle_n=float(grip_target_sensor_n.get("MIDDLE", 0.0)),
                touch_target_ring_n=float(grip_target_sensor_n.get("RING", 0.0)),
                touch_target_pinky_n=float(grip_target_sensor_n.get("PINKY", 0.0)),
                touch_target_active_count=int(len(get_active_grip_fsr_names())),
                # Page 1:
                # hand_*  = goc gesture/ban tay dau vao
                # unity_* = goc da shape gui sang Unity, hien thi khop hon voi Unity UI
                # auto_*  = hien tren Auto Command Bars, dung unity_* de tranh web gan 100 nhung Unity chi 70-80.
                hand_thumb_pct=(web_finger_raw_pct(robot_packet, "THUMB") if selected_hand_detected else 0.0),
                hand_index_pct=(web_finger_raw_pct(robot_packet, "INDEX") if selected_hand_detected else 0.0),
                hand_middle_pct=(web_finger_raw_pct(robot_packet, "MIDDLE") if selected_hand_detected else 0.0),
                hand_ring_pct=(web_finger_raw_pct(robot_packet, "RING") if selected_hand_detected else 0.0),
                hand_pinky_pct=(web_finger_raw_pct(robot_packet, "PINKY") if selected_hand_detected else 0.0),
                unity_thumb_pct=(web_finger_unity_pct(unity_motion_packet, "THUMB") if selected_hand_detected else 0.0),
                unity_index_pct=(web_finger_unity_pct(unity_motion_packet, "INDEX") if selected_hand_detected else 0.0),
                unity_middle_pct=(web_finger_unity_pct(unity_motion_packet, "MIDDLE") if selected_hand_detected else 0.0),
                unity_ring_pct=(web_finger_unity_pct(unity_motion_packet, "RING") if selected_hand_detected else 0.0),
                unity_pinky_pct=(web_finger_unity_pct(unity_motion_packet, "PINKY") if selected_hand_detected else 0.0),
                auto_thumb_pct=(web_finger_unity_pct(unity_motion_packet, "THUMB") if selected_hand_detected else 0.0),
                auto_index_pct=(web_finger_unity_pct(unity_motion_packet, "INDEX") if selected_hand_detected else 0.0),
                auto_middle_pct=(web_finger_unity_pct(unity_motion_packet, "MIDDLE") if selected_hand_detected else 0.0),
                auto_ring_pct=(web_finger_unity_pct(unity_motion_packet, "RING") if selected_hand_detected else 0.0),
                auto_pinky_pct=(web_finger_unity_pct(unity_motion_packet, "PINKY") if selected_hand_detected else 0.0),
                manual_thumb_pct=float(manual_target_pct.get("THUMB", 0.0)),
                manual_index_pct=float(manual_target_pct.get("INDEX", 0.0)),
                manual_middle_pct=float(manual_target_pct.get("MIDDLE", 0.0)),
                manual_ring_pct=float(manual_target_pct.get("RING", 0.0)),
                manual_pinky_pct=float(manual_target_pct.get("PINKY", 0.0)),
            )
        except Exception as e:
            # Khong de loi web lam dung code chinh.
            pass

    if auto_state is not None:
        unity_full_packet.update({
            "auto_grip_state": auto_state.state,
            "auto_grip_status": auto_state.status,
            "force_hold_allowed": bool(auto_state.force_hold_allowed),
            "palm_key_unlocked": bool(palm_key_unlocked),
            "palm_contact_raw": bool(palm_contact_raw),
            "palm_contact_hw": bool(palm_contact_hw),
            "palm_sim_enabled": bool(palm_sim_enabled),
            "palm_one_touch_unlocked": bool(palm_key_unlocked),
            "palm_unlock_source": str(palm_unlock_source),
            "palm_unlocked_object_label": str(palm_unlocked_object_label),
            "palm_wait_fresh_touch_after_object_change": bool(palm_wait_fresh_touch_after_object_change),
        })

    # ===================== WEB DASHBOARD DATA UPDATE - NO CAMERA STREAM =====================
    # Gui thong so that len web. Khong gui camera len web de tranh nang may.
    if web_dashboard_ok and update_dashboard is not None and (now - last_web_update) >= WEB_DASHBOARD_DATA_INTERVAL:
        last_web_update = now
        try:
            obj_label_web = "none"
            obj_name_web = "None"
            obj_mass_g_web = 0.0

            if unity_object_latch_label != "none":
                obj_label_web = normalize_object_label_for_unity(unity_object_latch_label)
                obj_mass_g_web = float(unity_object_latch_mass_g or 0.0)

            if obj_label_web == "kimquat_sauce":
                obj_name_web = "Kimquat Sauce"
                if obj_mass_g_web <= 0.0:
                    obj_mass_g_web = 200.0
            elif obj_label_web == "red_chili":
                obj_name_web = "Red Chili"
                if obj_mass_g_web <= 0.0:
                    obj_mass_g_web = 80.0
            elif obj_label_web == "orange":
                obj_name_web = "Orange"
                if obj_mass_g_web <= 0.0:
                    obj_mass_g_web = 150.0
            else:
                obj_label_web = "none"
                obj_name_web = "None"
                if manual_loadcell_override and manual_loadcell_g >= 30.0:
                    obj_name_web = "Manual Load"
                    obj_mass_g_web = float(manual_loadcell_g)
                else:
                    obj_mass_g_web = 0.0

            grip_state_web = str(auto_state.state) if auto_state is not None else "UNKNOWN"
            auto_state_web = str(auto_state.status) if auto_state is not None else "INIT"
            esp32_web = "Connected" if ser is not None else ("Disabled" if not SERIAL_ENABLE else "Disconnected")
            unity_web = f"UDP {UNITY_PORT}/{UNITY_OBJECT_PORT} OK"
            camera_status_web = f"MAIN:{'OK' if main_detected else 'NO'} PHONE:{'OK' if phone_detected else 'NO'}"
            yolo_status_web = "Ready" if obj_label_web == "none" else f"Detect {obj_label_web}"

            update_dashboard(
                object_label=obj_label_web,
                object_name=obj_name_web,
                mass_g=obj_mass_g_web,
                grip_state=grip_state_web,
                palm_sensor=bool(palm_contact_hw or (WEB_SHOW_PALM_UNLOCK_AS_SENSOR_ON and palm_key_unlocked)),
                palm_test=bool(palm_sim_enabled),
                loadcell_n=float(loadcell_n),
                required_min_n=float(force_dashboard_packet.get("required_min_n", 0.0)),
                required_max_n=float(force_dashboard_packet.get("required_max_n", 0.0)),
                target_total_n=float(force_dashboard_packet.get("recommended_n", 0.0)),
                fps=float(1.0 / max(dt, 1e-6)),
                esp32_status=esp32_web,
                unity_status=unity_web,
                serial_note=str(last_serial_note) if last_serial_note else "",
                mode=str(CONTROL_MODE),
                auto_state=auto_state_web,
                camera_status=camera_status_web,
                yolo_status=yolo_status_web,
                hand_status=f"ACTIVE {active_cam}",
                fsr_thumb_n=float(safe_fsr_n("THUMB")),
                fsr_index_n=float(safe_fsr_n("INDEX")),
                fsr_middle_n=float(safe_fsr_n("MIDDLE")),
                fsr_ring_n=float(safe_fsr_n("RING")),
                fsr_pinky_n=float(safe_fsr_n("PINKY")),
                target_thumb_n=float(initial_target_sensor_n.get("THUMB", 0.0)),
                target_index_n=float(initial_target_sensor_n.get("INDEX", 0.0)),
                target_middle_n=float(initial_target_sensor_n.get("MIDDLE", 0.0)),
                target_ring_n=float(initial_target_sensor_n.get("RING", 0.0)),
                target_pinky_n=float(initial_target_sensor_n.get("PINKY", 0.0)),
                touch_target_thumb_n=float(grip_target_sensor_n.get("THUMB", 0.0)),
                touch_target_index_n=float(grip_target_sensor_n.get("INDEX", 0.0)),
                touch_target_middle_n=float(grip_target_sensor_n.get("MIDDLE", 0.0)),
                touch_target_ring_n=float(grip_target_sensor_n.get("RING", 0.0)),
                touch_target_pinky_n=float(grip_target_sensor_n.get("PINKY", 0.0)),
                touch_target_active_count=int(len(get_active_grip_fsr_names())),
                # Page 1:
                # hand_*  = goc gesture/ban tay dau vao
                # unity_* = goc da shape gui sang Unity, hien thi khop hon voi Unity UI
                # auto_*  = hien tren Auto Command Bars, dung unity_* de tranh web gan 100 nhung Unity chi 70-80.
                hand_thumb_pct=(web_finger_raw_pct(robot_packet, "THUMB") if selected_hand_detected else 0.0),
                hand_index_pct=(web_finger_raw_pct(robot_packet, "INDEX") if selected_hand_detected else 0.0),
                hand_middle_pct=(web_finger_raw_pct(robot_packet, "MIDDLE") if selected_hand_detected else 0.0),
                hand_ring_pct=(web_finger_raw_pct(robot_packet, "RING") if selected_hand_detected else 0.0),
                hand_pinky_pct=(web_finger_raw_pct(robot_packet, "PINKY") if selected_hand_detected else 0.0),
                unity_thumb_pct=(web_finger_unity_pct(unity_motion_packet, "THUMB") if selected_hand_detected else 0.0),
                unity_index_pct=(web_finger_unity_pct(unity_motion_packet, "INDEX") if selected_hand_detected else 0.0),
                unity_middle_pct=(web_finger_unity_pct(unity_motion_packet, "MIDDLE") if selected_hand_detected else 0.0),
                unity_ring_pct=(web_finger_unity_pct(unity_motion_packet, "RING") if selected_hand_detected else 0.0),
                unity_pinky_pct=(web_finger_unity_pct(unity_motion_packet, "PINKY") if selected_hand_detected else 0.0),
                auto_thumb_pct=(web_finger_unity_pct(unity_motion_packet, "THUMB") if selected_hand_detected else 0.0),
                auto_index_pct=(web_finger_unity_pct(unity_motion_packet, "INDEX") if selected_hand_detected else 0.0),
                auto_middle_pct=(web_finger_unity_pct(unity_motion_packet, "MIDDLE") if selected_hand_detected else 0.0),
                auto_ring_pct=(web_finger_unity_pct(unity_motion_packet, "RING") if selected_hand_detected else 0.0),
                auto_pinky_pct=(web_finger_unity_pct(unity_motion_packet, "PINKY") if selected_hand_detected else 0.0),
                manual_thumb_pct=float(manual_target_pct.get("THUMB", 0.0)),
                manual_index_pct=float(manual_target_pct.get("INDEX", 0.0)),
                manual_middle_pct=float(manual_target_pct.get("MIDDLE", 0.0)),
                manual_ring_pct=float(manual_target_pct.get("RING", 0.0)),
                manual_pinky_pct=float(manual_target_pct.get("PINKY", 0.0)),
            )
        except Exception:
            pass

    # Gui UI Force Analysis toi Unity khoang 30 FPS, du muot va khong spam UDP qua nhieu.
    UNITY_FORCE_SEND_INTERVAL = 1.0 / 30.0

    if (
        packet_diff(unity_motion_packet, last_unity_sent_packet) >= SEND_EPS
        or (now - last_send_time) >= UNITY_FORCE_SEND_INTERVAL
    ):
        msg = json.dumps(unity_full_packet).encode("utf-8")
        sock.sendto(msg, (UNITY_IP, UNITY_PORT))

        # Chi luu motion packet vi last_unity_sent_packet chi co 15 key ngon tay.
        last_unity_sent_packet = packet_copy(unity_motion_packet)
        last_send_time = now

    if ser is not None and (now - last_serial_time) >= SERIAL_SEND_INTERVAL:
        if SERIAL_MODE == "ALL_5":
            try:
                for name in ["INDEX", "MIDDLE", "RING", "PINKY", "THUMB"]:
                    raw_pct = build_serial_percent(robot_packet, name, now, fsr_pct, last_fsr_rx_time)
                    pct = servo_postprocess(name, raw_pct, last_serial_pct[name], servo_hist, servo_ema)
                    servo_dbg[name] = pct
                    if last_serial_pct[name] < 0 or abs(pct - last_serial_pct[name]) >= SERIAL_SEND_DELTA:
                        serial_send_line(f"{name}_PCT {pct}")
                        last_serial_pct[name] = pct
            except Exception as e:
                print(f"Serial write fail: {e}")
                try:
                    ser.close()
                except Exception:
                    pass
                ser = None
        last_serial_time = now

    # ===================== COMPACT PYTHON CAMERA UI =====================
    # Ban thu gon: chi ve camera + nut Palm Sensor, tat toan bo slider/thong so cu.
    ui_hitboxes.clear()

    fps = 1.0 / max(dt, 1e-6)
    compact_obj_label = "none"
    if object_result is not None:
        compact_obj_label = normalize_object_label_for_unity(object_result.stable_label)

    if PYTHON_PREVIEW_ENABLE:
        display = build_compact_camera_ui(
            frame_main=frame_main,
            frame_phone=frame_phone,
            active_cam=active_cam,
            main_detected=main_detected,
            phone_detected=phone_detected,
            palm_raw=bool(palm_contact_hw),
            palm_sim=bool(palm_sim_enabled),
            obj_label=compact_obj_label,
        )
        cv2.imshow(WIN_NAME, display)

    key = cv2.waitKey(1) & 0xFF

    # Neu dang nhap khoi luong tren dashboard thi phim se vao o input.
    if manual_load_input_active and key != 255:
        if key == 13 or key == 10:  # Enter
            commit_manual_load_input()
        elif key == 8:              # Backspace
            manual_load_input_text = manual_load_input_text[:-1]
        elif key == 27:             # Esc
            manual_load_input_active = False
            manual_load_input_text = ""
        else:
            ch = chr(key) if 0 <= key <= 255 else ""
            if (ch.isdigit() or ch == ".") and len(manual_load_input_text) < 7:
                manual_load_input_text += ch

        # Khong cho phim vua nhap khoi luong vua kich hoat shortcut khac.
        key = 255

    if key == ord('q') or key == ord('Q'):
        break
    elif key == ord('p') or key == ord('P'):
        # Shortcut phu. Neu cam bien long ban tay that dang ON thi khoa PALM TEST.
        if palm_sensor_contact():
            palm_sim_enabled = False
            palm_key_unlocked = True
            last_serial_note = "PALM HW ACTIVE - TEST LOCKED"
            print("[PALM SIM LOCKED] Hardware palm sensor is ON")
        else:
            palm_sim_enabled = not palm_sim_enabled
            if not palm_sim_enabled:
                palm_key_unlocked = False
            last_serial_note = "PALM SIM ON" if palm_sim_enabled else "PALM SIM OFF"
            print("[PALM SIM]", "ON" if palm_sim_enabled else "OFF")

cap_main.release()
if cap_phone is not None:
    cap_phone.release()
cv2.destroyAllWindows()
hands_main.close()
hands_phone.close()
sock.close()
try:
    object_sock.close()
except Exception:
    pass
if ser is not None:
    try:
        serial_send_line("FSR_STREAM_OFF")
        serial_send_line("LOAD_STREAM_OFF")
    except Exception:
        pass
    ser.close()
