# ============================================================
# FILE 2/3
# COPY VAO FILE:
# datn_auto_grip_state.py
#
# DATN - AUTO GRIP STATE MACHINE
#
# Logic:
# - FREE_TRACKING: robot di theo tay binh thuong.
# - Khi YOLO thay 1 trong 3 vat -> OBJECT_LOCKED.
# - Neu PALM_GATE_LOCKED=True: can palm sensor cham 1 lan de unlock.
# - Sau khi unlock -> VISION_CLOSING, robot follow tay de nam vat.
# - Khi FSR ngon INDEX/MIDDLE/RING/PINKY cham vat -> FORCE_HOLD.
# - FORCE_HOLD bo qua jitter landmark, chi mo khi nguoi dung mo tay.
#
# Object hop le:
# - kimquat_sauce: 200g
# - red_chili    : 80g
# - orange       : 150g
# ============================================================

import time
from dataclasses import dataclass
from collections import deque


VALID_OBJECT_LABELS = ("kimquat_sauce", "red_chili", "orange")


@dataclass
class GripStateOutput:
    state: str
    mode_text: str
    force_hold_allowed: bool
    vision_follow_allowed: bool
    object_label: str
    mass_g: float
    status: str


class AutoGripStateMachine:
    FREE_TRACKING = "FREE_TRACKING_AUTO"
    OBJECT_LOCKED = "OBJECT_LOCKED_AUTO"
    WAIT_PALM = "WAIT_PALM_CONTACT_AUTO"
    WAIT_GRIP = "WAIT_HUMAN_GRIP_AUTO"
    VISION_CLOSING = "VISION_GRIP_CLOSING_AUTO"
    FORCE_HOLD = "FORCE_HOLD_AUTO"
    RELEASE = "RELEASE_OBJECT_AUTO"

    def __init__(
        self,
        close_enter=35.0,
        open_enter=28.0,
        hand_stable_frames=5,
        object_timeout_sec=2.0,
        release_sec=0.8,
    ):
        self.state = self.FREE_TRACKING
        self.close_enter = float(close_enter)
        self.open_enter = float(open_enter)
        self.hand_stable_frames = max(3, int(hand_stable_frames))
        self.object_timeout_sec = float(object_timeout_sec)
        self.release_sec = float(release_sec)

        self.object_label = "none"
        self.mass_g = 0.0
        self.object_last_seen_time = 0.0

        # Palm sensor la chia khoa mo khoa 1 lan.
        self.grip_unlocked = False

        self.close_hist = deque(maxlen=self.hand_stable_frames)
        self.open_hist = deque(maxlen=self.hand_stable_frames)

        self.last_state_change = time.time()

    @staticmethod
    def hand_close_level(packet):
        groups = [
            ("thumb1", "thumb2", "thumb3"),
            ("index1", "index2", "index3"),
            ("middle1", "middle2", "middle3"),
            ("ring1", "ring2", "ring3"),
            ("pinky1", "pinky2", "pinky3"),
        ]

        vals = []
        for keys in groups:
            vals.append(sum(float(packet.get(k, 0.0)) for k in keys) / 3.0)

        return max(vals) if vals else 0.0

    def _stable_true(self, hist):
        return len(hist) == hist.maxlen and all(hist)

    def reset_to_free(self):
        self.state = self.FREE_TRACKING
        self.object_label = "none"
        self.mass_g = 0.0
        self.grip_unlocked = False
        self.close_hist.clear()
        self.open_hist.clear()
        self.last_state_change = time.time()

    def change_state(self, new_state):
        if new_state != self.state:
            self.state = new_state
            self.last_state_change = time.time()

    def update(
        self,
        object_label,
        object_mass_g,
        palm_contact,
        any_finger_contact,
        hand_detected,
        robot_packet,
        manual_mass_active=False,
    ):
        now = time.time()
        close_level = self.hand_close_level(robot_packet)

        is_closing = bool(hand_detected and close_level >= self.close_enter)
        is_opening = bool(hand_detected and close_level <= self.open_enter)

        self.close_hist.append(is_closing)
        self.open_hist.append(is_opening)

        stable_open = self._stable_true(self.open_hist)

        object_valid = object_label in VALID_OBJECT_LABELS or manual_mass_active

        if object_label in VALID_OBJECT_LABELS:
            self.object_label = object_label
            self.mass_g = float(object_mass_g)
            self.object_last_seen_time = now

        if manual_mass_active and object_label == "none":
            self.object_label = "manual"
            self.object_last_seen_time = now

        object_recent = (now - self.object_last_seen_time) <= self.object_timeout_sec

        # ===================== STATE TRANSITIONS =====================
        if self.state == self.FREE_TRACKING:
            if object_valid:
                self.change_state(self.OBJECT_LOCKED)

        elif self.state == self.OBJECT_LOCKED:
            if not object_recent and not manual_mass_active:
                self.reset_to_free()
            elif palm_contact or self.grip_unlocked:
                self.grip_unlocked = True
                self.change_state(self.VISION_CLOSING)
            else:
                self.change_state(self.WAIT_PALM)

        elif self.state == self.WAIT_PALM:
            if not object_recent and not manual_mass_active:
                self.reset_to_free()
            elif palm_contact or self.grip_unlocked:
                self.grip_unlocked = True
                self.change_state(self.VISION_CLOSING)

        elif self.state == self.WAIT_GRIP:
            # Giu lai de tuong thich UI cu.
            if palm_contact:
                self.grip_unlocked = True

            if self.grip_unlocked:
                self.change_state(self.VISION_CLOSING)
            else:
                self.change_state(self.WAIT_PALM)

        elif self.state == self.VISION_CLOSING:
            if any_finger_contact:
                self.change_state(self.FORCE_HOLD)
            elif stable_open:
                self.change_state(self.RELEASE)

        elif self.state == self.FORCE_HOLD:
            # Chi thoat khi nguoi dung mo tay ro rang.
            if stable_open:
                self.change_state(self.RELEASE)

        elif self.state == self.RELEASE:
            if (now - self.last_state_change) > self.release_sec:
                self.reset_to_free()

        force_hold_allowed = self.state == self.FORCE_HOLD

        # Chỉ follow landmark khi free hoặc dang vision closing.
        # WAIT_PALM/OBJECT_LOCKED khong follow de tranh gap tay khi vat chua cham palm.
        vision_follow_allowed = self.state in (
            self.FREE_TRACKING,
            self.VISION_CLOSING,
        )

        if self.state == self.FREE_TRACKING:
            status = "FREE TRACKING - robot follows hand"
        elif self.state == self.OBJECT_LOCKED:
            status = "OBJECT LOCKED - waiting palm sensor"
        elif self.state == self.WAIT_PALM:
            status = "WAIT PALM KEY TOUCH"
        elif self.state == self.WAIT_GRIP:
            status = "PALM CONTACT - UNLOCK FOLLOW"
        elif self.state == self.VISION_CLOSING:
            status = "PALM UNLOCKED - FOLLOW HAND"
        elif self.state == self.FORCE_HOLD:
            status = "FORCE HOLD - ignore hand jitter"
        elif self.state == self.RELEASE:
            status = "RELEASE OBJECT"
        else:
            status = self.state

        return GripStateOutput(
            state=self.state,
            mode_text="AUTO",
            force_hold_allowed=force_hold_allowed,
            vision_follow_allowed=vision_follow_allowed,
            object_label=self.object_label,
            mass_g=self.mass_g,
            status=status,
        )
