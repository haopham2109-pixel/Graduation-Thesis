#include <ESP32Servo.h>
#include "HX711.h"
#include <ctype.h>
#include <string.h>
#include <stdlib.h>
  #include <math.h>

enum FingerIndex {
  IDX_PINKY = 0,
  IDX_RING,
  IDX_MIDDLE,
  IDX_INDEX,
  IDX_THUMB,
  FINGER_COUNT
};

Servo servoPinky;
Servo servoRing;
Servo servoMiddle;
Servo servoIndex;
Servo servoThumb;

Servo* servos[FINGER_COUNT] = {
  &servoPinky,
  &servoRing,
  &servoMiddle,
  &servoIndex,
  &servoThumb
};

const char* fingerNames[FINGER_COUNT] = {
  "PINKY",
  "RING",
  "MIDDLE",
  "INDEX",
  "THUMB"
};

const int servoPins[FINGER_COUNT] = {
  32,
  33,
  25,
  26,
  27
};

const int fsrPins[FINGER_COUNT] = {
  34,   // PINKY
  35,   // RING
  14,   // MIDDLE
  12,   // INDEX  (SN/SM on some boards)
  36      // THUMB  (SP on some boards)
};

// ============================================================
// DATN V10 - MICROSECOND SERVO MODE
// Gia tri openAngle/closeAngle ben duoi la xung microsecond, KHONG PHAI do 0-180.
// Thu tu: PINKY, RING, MIDDLE, INDEX, THUMB
// Bang nay lay theo ket qua test thuc te cua ban:
// PINKY  open=2000us, close=400us
// RING   open=2000us, close=400us
// MIDDLE open=500us,  close=2100us
// INDEX  open=500us,  close=2300us
// THUMB  open=500us,  close=1300us
// ============================================================
const int SERVO_US_MIN = 400;
const int SERVO_US_MAX = 2500;

const int openAngle[FINGER_COUNT] = {
  2300,  // PINKY
  1800,  // RING
  800,   // MIDDLE
  800,   // INDEX
  800    // THUMB
};

const int closeAngle[FINGER_COUNT] = {
  1000,   // PINKY
  800,   // RING
  2000,  // MIDDLE
  1950,  // INDEX
  1500   // THUMB
};

float currentCmd[FINGER_COUNT];
float targetCmd[FINGER_COUNT];
int lastWritten[FINGER_COUNT];

// V10: don vi bay gio la microsecond/second, khong phai degree/second.
const float maxDegPerSec[FINGER_COUNT] = {
  2200.0f,  // PINKY
  2200.0f,  // RING
  2200.0f,  // MIDDLE
  2400.0f,  // INDEX
  1500.0f   // THUMB
};

bool assistActive[FINGER_COUNT] = {false, false, false, false, false};
float assistFinalTarget[FINGER_COUNT] = {0, 0, 0, 0, 0};
float assistBoostTarget[FINGER_COUNT] = {0, 0, 0, 0, 0};
unsigned long assistUntilMs[FINGER_COUNT] = {0, 0, 0, 0, 0};

const int PINKY_CLOSE_BOOST_ANGLE = 146;
const int PINKY_CLOSE_BOOST_MS = 65;
const int RING_OPEN_BOOST_ANGLE = 168;
const int RING_OPEN_BOOST_MS = 70;
const int THUMB_CLOSE_BOOST_ANGLE = 9;
const int THUMB_CLOSE_BOOST_MS = 65;

const unsigned long SERVO_UPDATE_MS = 8;
unsigned long lastServoUpdateMs = 0;

// FSR
int fsrRaw[FINGER_COUNT] = {0, 0, 0, 0, 0};
float fsrFilt[FINGER_COUNT] = {0, 0, 0, 0, 0};
float fsrPct[FINGER_COUNT] = {0, 0, 0, 0, 0};

// FSR ADC -> Force N
// Huong noi khuyen nghi: 3V3 -> FSR -> ADC -> R 10k -> GND, luc tang thi ADC tang.
//
// FIX QUAN TRONG:
// Ban cu doi luc tu ADC raw truc tiep => neu ADC khi khong cham da cao, luc N se bi ao.
// Ban nay doi theo ADC_DELTA = ADC_da_loc - ADC_khong_cham(fsrOpenCal) - deadband.
// Muon do chinh xac, moi ngon phai calib rieng bang vat/qua can chuan.
float fsrForceN[FINGER_COUNT] = {0, 0, 0, 0, 0};
float forceThresholdN[FINGER_COUNT] = {4.0f, 4.0f, 4.0f, 4.0f, 4.0f};
bool forceLimitUseNewton = false;

const int FSR_CAL_POINT_COUNT = 3;

  // ============================================================
  // DATN V38 - FSR REAL CALIB TABLE
  // ============================================================
  // Bang nay da thay bang so do thuc te tu 6 vat mau:
  // - OTBOT 104g     -> 1.020N
  // - TAYNINH 112g   -> 1.099N
  // - CAFESUA 224g   -> 2.197N
  // - KIMQUAT 227g   -> 2.227N
  // - REVIVE 531g    -> 5.209N
  // - XIDAU 573g     -> 5.621N
  //
  // Du lieu thuc te co hien tuong FSR bao hoa/lech vi tri ep,
  // nen khong dua ca 6 moc rieng vao bang vi co diem ADC khong tang deu.
  // Ta gom thanh 3 cum luc trung binh:
  // - 1.06N = trung binh OTBOT + TAYNINH
  // - 2.21N = trung binh CAFESUA + KIMQUAT
  // - 5.42N = trung binh REVIVE + XIDAU
  //
  // Gia tri bang ADC la AVG_TABLE_DELTA, tuc da tru baseline va deadband.
  // ============================================================
  const float FSR_FORCE_TABLE_N[FSR_CAL_POINT_COUNT] = {
    0.00f,
    1.06f,  // trung binh OTBOT 1.020N + TAYNINH 1.099N
    2.10f   // trung binh ORANGE 1.962N + KIMQUAT 2.227N
  };

  // Deadband ADC sau khi tru baseline. Tang len neu chua cham ma van nhay luc.
  int fsrForceDeadbandAdc[FINGER_COUNT] = {
    60, 60, 60, 60, 60
  };

  // Thu tu: PINKY, RING, MIDDLE, INDEX, THUMB
  // Gia tri trong bang la ADC_DELTA sau khi tru baseline + deadband, KHONG phai ADC raw.
  //
  // Bang trung binh tu du lieu do:
  //            1.06N   2.21N   5.42N
  // PINKY  :   1202    1624    1908
  // RING   :   1940    2128    2323
  // MIDDLE :   1777    2338    2516
  // INDEX  :    780    1424    1870
  //
  // V55:
  // THUMB bay gio la cam bien luc rieng cua ngon cai, KHONG con la palm sensor.
  // Palm key/sensor doc rieng o PALM_KEY_PIN ben duoi.
  int fsrDeltaAdcTable[FINGER_COUNT][FSR_CAL_POINT_COUNT] = {
    // Thu tu: PINKY, RING, MIDDLE, INDEX, THUMB
    // 0N, 1.06N, 2.10N
    // Bang moi tinh tu 4 vat: OTBOT, TAYNINH, ORANGE, KIMQUAT.
    // OTBOT + TAYNINH duoc gom thanh moc thap 1.06N.
    // ORANGE + KIMQUAT duoc gom thanh moc cao 2.10N.
    // Ly do: FSR co diem bi bao hoa/khong tang deu, nen khong nen dua 4 moc rieng vao bang noi suy.
    {0, 1660, 2017},  // PINKY
    {0, 1625, 2168},  // RING
    {0, 1685, 2021},  // MIDDLE
    {0, 1571, 2080},  // INDEX
    {0, 3476, 3862}   // THUMB - FSR402 GPIO36
  };

  int fsrOpenCal[FINGER_COUNT] = {0, 0, 0, 0, 0};
int fsrCloseCal[FINGER_COUNT] = {4095, 4095, 4095, 4095, 4095};
bool fsrInitDone[FINGER_COUNT] = {false, false, false, false, false};

const float FSR_EMA_ALPHA = 0.20f;
const unsigned long FSR_UPDATE_MS = 10;
unsigned long lastFsrUpdateMs = 0;

bool fsrStreamEnable = false;
const unsigned long FSR_STREAM_MS = 110;
unsigned long lastFsrStreamMs = 0;

bool forceLimitEnable = false;
float forceThresholdPct[FINGER_COUNT] = {74.0f, 74.0f, 92.0f, 78.0f, 82.0f};
unsigned long forceHitHoldUntil[FINGER_COUNT] = {0, 0, 0, 0, 0};
const unsigned long FORCE_HIT_HOLD_MS = 300;

// HX711 + Loadcell 5kg
// Noi day: HX711 VCC->3V3, GND->GND, DT/DOUT->GPIO19, SCK/CLK->GPIO18
const int LOADCELL_DT_PIN = 19;
const int LOADCELL_SCK_PIN = 18;

HX711 loadcell;
bool loadcellInitDone = false;
bool loadcellStreamEnable = false;

float loadcellCalibration = 7050.0f;  // Can calib lai bang qua can that
float loadcellKg = 0.0f;
float loadcellN = 0.0f;
long loadcellRaw = 0;

const unsigned long LOADCELL_UPDATE_MS = 120;
const unsigned long LOADCELL_STREAM_MS = 250;
unsigned long lastLoadcellUpdateMs = 0;
unsigned long lastLoadcellStreamMs = 0;

// ===================== INDEPENDENT PALM KEY / PALM SENSOR =====================
// V55: Palm khong con dung chung voi THUMB FSR.
// Mac dinh dung analog palm sensor tren GPIO39 (ADC1, input-only).
// Neu ban dung nut digital co dien tro keo ngoai, doi PALM_KEY_ANALOG=false.
// Palm unlock tren PCB van giu GPIO13. THUMB FSR402 moi dung GPIO36.
const int PALM_KEY_PIN = 13;
const bool PALM_KEY_ANALOG = true;
const int PALM_KEY_ADC_THRESHOLD = 500;  // sua sau khi test ADC palm
const bool PALM_KEY_ACTIVE_HIGH = true;
const float PALM_KEY_EMA_ALPHA = 0.25f;
int palmKeyRaw = 0;
float palmKeyFilt = 0.0f;
bool palmKeyContact = false;
const unsigned long PALM_KEY_UPDATE_MS = 10;
unsigned long lastPalmKeyUpdateMs = 0;

// Thumb stagger close: close 4 fingers first, THUMB closes after delay
bool thumbDelayEnable = true;
unsigned long thumbCloseDelayMs = 1000;
bool thumbDelayActive = false;
float thumbDelayedTarget = 150.0f;
unsigned long thumbDelayUntilMs = 0;

// Extra squeeze assist by FSR:
// Khi ngon da gap kha sau ma FSR van chua len, servo se keo them mot doan nho
// de ep vat vao FSR. Force limit van uu tien dung lai neu luc da du.
// LAN9: noi rieng cho MIDDLE de ngoi giua gap sau hon, tranh bi dung som.
// V10: tam tat squeeze assist vi cac gia tri extra cu theo degree, chua doi sang microsecond.
bool squeezeAssistEnable = false;
float squeezeEngageClosePct[FINGER_COUNT] = {78.0f, 80.0f, 74.0f, 80.0f, 78.0f};
float squeezeTouchPct[FINGER_COUNT]      = {14.0f, 14.0f, 10.0f, 16.0f, 18.0f};
float squeezeMaxExtraDeg[FINGER_COUNT]   = {12.0f, 8.0f, 14.0f, 8.0f, 12.0f};
float squeezeGainDegPerPct[FINGER_COUNT] = {0.70f, 0.55f, 0.80f, 0.55f, 0.75f};

char rxBuf[96];
size_t rxLen = 0;

void applySmartTarget(int idx, int ang);


int clampInt(int v, int lo, int hi) {
  if (v < lo) return lo;
  if (v > hi) return hi;
  return v;
}

float clampFloat(float v, float lo, float hi) {
  if (v < lo) return lo;
  if (v > hi) return hi;
  return v;
}

bool endsWith(const char* s, const char* suffix) {
  size_t ls = strlen(s);
  size_t lf = strlen(suffix);
  if (lf > ls) return false;
  return strcmp(s + ls - lf, suffix) == 0;
}

void trimInPlace(char* s) {
  if (!s) return;

  while (*s && isspace((unsigned char)*s)) {
    memmove(s, s + 1, strlen(s));
  }

  int n = strlen(s);
  while (n > 0 && isspace((unsigned char)s[n - 1])) {
    s[n - 1] = '\0';
    n--;
  }
}

void upperInPlace(char* s) {
  while (*s) {
    *s = (char)toupper((unsigned char)*s);
    s++;
  }
}

int pctToAngle(int pct, int openAng, int closeAng) {
  pct = clampInt(pct, 0, 100);
  float t = pct / 100.0f;
  float ang = openAng + (closeAng - openAng) * t;
  return (int)(ang + 0.5f);
}

float angleToClosePct(int idx, float ang) {
  float den = (float)closeAngle[idx] - (float)openAngle[idx];
  if (fabs(den) < 1e-6f) return 0.0f;
  return (ang - (float)openAngle[idx]) / den;
}

float angleToClosePct100(int idx, float ang) {
  return clampFloat(angleToClosePct(idx, ang) * 100.0f, 0.0f, 100.0f);
}

float moveTowardCloseByExtraDeg(int idx, float baseAng, float extraDeg) {
  float sign = ((float)closeAngle[idx] >= (float)openAngle[idx]) ? 1.0f : -1.0f;
  return clampFloat(baseAng + sign * extraDeg, (float)SERVO_US_MIN, (float)SERVO_US_MAX);
}

float computeSqueezeTarget(int idx, float baseTgt) {
  if (!squeezeAssistEnable) return baseTgt;

  float closePct = angleToClosePct100(idx, baseTgt);
  if (closePct < squeezeEngageClosePct[idx]) return baseTgt;

  bool closing = isClosingTowardTarget(idx, currentCmd[idx], baseTgt);
  if (!closing) return baseTgt;

  float deficit = squeezeTouchPct[idx] - fsrPct[idx];
  if (deficit <= 0.0f) return baseTgt;

  float extra = deficit * squeezeGainDegPerPct[idx];
  extra = clampFloat(extra, 0.0f, squeezeMaxExtraDeg[idx]);

  return moveTowardCloseByExtraDeg(idx, baseTgt, extra);
}

bool isClosingTowardTarget(int idx, float curAng, float tgtAng) {
  return angleToClosePct(idx, tgtAng) > angleToClosePct(idx, curAng) + 1e-4f;
}

int getFingerIndex(const char* name) {
  for (int i = 0; i < FINGER_COUNT; i++) {
    if (strcmp(name, fingerNames[i]) == 0) return i;
  }
  return -1;
}

void cancelAssist(int idx) {
  assistActive[idx] = false;
}

void startAssist(int idx, int boostTarget, int finalTarget, int holdMs) {
  assistActive[idx] = true;
  assistBoostTarget[idx] = clampFloat((float)boostTarget, (float)SERVO_US_MIN, (float)SERVO_US_MAX);
  assistFinalTarget[idx] = clampFloat((float)finalTarget, (float)SERVO_US_MIN, (float)SERVO_US_MAX);
  assistUntilMs[idx] = millis() + (unsigned long)holdMs;
  targetCmd[idx] = assistBoostTarget[idx];
}

void setTargetRaw(int idx, int ang) {
  cancelAssist(idx);
  targetCmd[idx] = clampFloat((float)ang, (float)SERVO_US_MIN, (float)SERVO_US_MAX);
}

void cancelThumbDelay() {
  thumbDelayActive = false;
}

void updateThumbDelayState() {
  if (!thumbDelayEnable || !thumbDelayActive) return;
  unsigned long now = millis();
  if ((long)(now - thumbDelayUntilMs) >= 0) {
    float finalAng = clampFloat(thumbDelayedTarget, (float)SERVO_US_MIN, (float)SERVO_US_MAX);
    thumbDelayActive = false;
    applySmartTarget(IDX_THUMB, (int)(finalAng + 0.5f));
  }
}

void setThumbTargetStaggered(int ang) {
  ang = clampInt(ang, SERVO_US_MIN, SERVO_US_MAX);

  if (!thumbDelayEnable) {
    cancelThumbDelay();
    applySmartTarget(IDX_THUMB, ang);
    return;
  }

  float cmdPct = angleToClosePct(IDX_THUMB, (float)ang);

  if (thumbDelayActive) {
    float delayedPct = angleToClosePct(IDX_THUMB, thumbDelayedTarget);

    if (cmdPct >= delayedPct - 1e-4f) {
      thumbDelayedTarget = clampFloat((float)ang, (float)SERVO_US_MIN, (float)SERVO_US_MAX);
      return;
    }

    cancelThumbDelay();
    applySmartTarget(IDX_THUMB, ang);
    return;
  }

  float curPct = angleToClosePct(IDX_THUMB, currentCmd[IDX_THUMB]);
  float tgtPct = angleToClosePct(IDX_THUMB, targetCmd[IDX_THUMB]);
  float refPct = (curPct > tgtPct) ? curPct : tgtPct;
  bool closingMore = (cmdPct > refPct + 1e-4f);

  if (closingMore) {
    cancelAssist(IDX_THUMB);
    thumbDelayActive = true;
    thumbDelayUntilMs = millis() + thumbCloseDelayMs;
    thumbDelayedTarget = clampFloat((float)ang, (float)SERVO_US_MIN, (float)SERVO_US_MAX);
    targetCmd[IDX_THUMB] = currentCmd[IDX_THUMB];
    return;
  }

  applySmartTarget(IDX_THUMB, ang);
}

void applySmartTarget(int idx, int ang) {
  // V10 MICROSECOND MODE:
  // Bo cac boost cu theo degree vi bay gio servo dung microsecond.
  // Force limit rieng tung ngon van xu ly trong updateServosNonBlocking().
  ang = clampInt(ang, SERVO_US_MIN, SERVO_US_MAX);
  setTargetRaw(idx, ang);
}

void updateAssistTargets() {
  unsigned long now = millis();
  for (int i = 0; i < FINGER_COUNT; i++) {
    if (!assistActive[i]) continue;
    if ((long)(now - assistUntilMs[i]) < 0) {
      targetCmd[i] = assistBoostTarget[i];
    } else {
      targetCmd[i] = assistFinalTarget[i];
      assistActive[i] = false;
    }
  }
}


float getFsrDeltaAdc(int idx, float adcRaw) {
  if (idx < 0 || idx >= FINGER_COUNT) return 0.0f;

  float delta = adcRaw - (float)fsrOpenCal[idx];

  // Tru deadband de chong nhay luc khi moi cham/nhieu ADC.
  delta -= (float)fsrForceDeadbandAdc[idx];

  if (delta < 0.0f) delta = 0.0f;
  return delta;
}

float interpolateFsrForceN(int idx, float adcRaw) {
  if (idx < 0 || idx >= FINGER_COUNT) return 0.0f;

  float adcDelta = getFsrDeltaAdc(idx, adcRaw);

  // ============================================================
  // V56 FIX - THUMB DYNAMIC FORCE SCALE
  // Lý do:
  // - THUMB table cũ cần DELTA rất cao: 3476 -> 1.06N, 3862 -> 2.10N.
  // - Nếu fsrOpenCal của THUMB sau restart lớn hơn ~173 ADC thì về mặt toán học
  //   gần như không thể đạt DELTA 3862 vì ADC max chỉ 4095.
  // - Do đó chạm mạnh có thể chỉ hiện 0.6N dù cảm biến còn hoạt động.
  //
  // Cách mới:
  // - Riêng THUMB dùng fsrCloseCal để map lực động.
  // - Mặc định fsrCloseCal = 4095, hoặc người dùng có thể giữ lực mạnh rồi gửi CALFSR_CLOSE.
  // - Khi adcDelta đạt thumbFullDelta thì THUMB_N = 2.10N.
  // ============================================================
  if (idx == IDX_THUMB) {
    float thumbFullDelta = getFsrDeltaAdc(idx, (float)fsrCloseCal[idx]);
    if (thumbFullDelta > 200.0f) {
      float n = (adcDelta / thumbFullDelta) * 2.10f;
      return clampFloat(n, 0.0f, 2.10f);
    }
  }

  int* adcTable = fsrDeltaAdcTable[idx];

  if (adcDelta <= adcTable[0]) return FSR_FORCE_TABLE_N[0];
  if (adcDelta >= adcTable[FSR_CAL_POINT_COUNT - 1]) return FSR_FORCE_TABLE_N[FSR_CAL_POINT_COUNT - 1];

  for (int i = 0; i < FSR_CAL_POINT_COUNT - 1; i++) {
    int a0 = adcTable[i];
    int a1 = adcTable[i + 1];

    if (a1 <= a0) continue;

    if (adcDelta >= (float)a0 && adcDelta <= (float)a1) {
      float ratio = (adcDelta - (float)a0) / ((float)a1 - (float)a0);
      return FSR_FORCE_TABLE_N[i] + ratio * (FSR_FORCE_TABLE_N[i + 1] - FSR_FORCE_TABLE_N[i]);
    }
  }

  return 0.0f;
}

void updateFsrNonBlocking() {
  unsigned long now = millis();
  if (now - lastFsrUpdateMs < FSR_UPDATE_MS) return;
  lastFsrUpdateMs = now;

  for (int i = 0; i < FINGER_COUNT; i++) {
    int raw = analogRead(fsrPins[i]);
    fsrRaw[i] = raw;

    if (!fsrInitDone[i]) {
      fsrFilt[i] = (float)raw;
      fsrOpenCal[i] = raw;
      fsrInitDone[i] = true;
    } else {
      fsrFilt[i] = fsrFilt[i] + ((float)raw - fsrFilt[i]) * FSR_EMA_ALPHA;
    }

    int lo = fsrOpenCal[i];
    int hi = fsrCloseCal[i];
    if (hi <= lo + 10) hi = lo + 10;

    float pct = ((fsrFilt[i] - (float)lo) * 100.0f) / ((float)hi - (float)lo);
    fsrPct[i] = clampFloat(pct, 0.0f, 100.0f);

    // Doi ADC da loc sang luc N bang bang noi suy
    fsrForceN[i] = interpolateFsrForceN(i, fsrFilt[i]);
  }
}

void updatePalmKeyNonBlocking() {
  unsigned long now = millis();
  if (now - lastPalmKeyUpdateMs < PALM_KEY_UPDATE_MS) return;
  lastPalmKeyUpdateMs = now;

  if (PALM_KEY_ANALOG) {
    int raw = analogRead(PALM_KEY_PIN);
    palmKeyRaw = raw;
    if (palmKeyFilt <= 0.1f) palmKeyFilt = (float)raw;
    else palmKeyFilt = palmKeyFilt + ((float)raw - palmKeyFilt) * PALM_KEY_EMA_ALPHA;

    bool active = ((int)(palmKeyFilt + 0.5f) >= PALM_KEY_ADC_THRESHOLD);
    palmKeyContact = PALM_KEY_ACTIVE_HIGH ? active : !active;
  } else {
    int raw = digitalRead(PALM_KEY_PIN);
    palmKeyRaw = raw;
    bool active = (raw != 0);
    palmKeyContact = PALM_KEY_ACTIVE_HIGH ? active : !active;
  }
}

void printPalmKeyReadback() {
  Serial.print("PALM_KEY CONTACT=");
  Serial.print(palmKeyContact ? 1 : 0);
  Serial.print(" RAW=");
  Serial.println((int)(palmKeyFilt + 0.5f));
}

void maybeStreamFsr() {
  if (!fsrStreamEnable) return;
  unsigned long now = millis();
  if (now - lastFsrStreamMs < FSR_STREAM_MS) return;
  lastFsrStreamMs = now;

  Serial.print("FSR_RAW ");
  for (int i = 0; i < FINGER_COUNT; i++) {
    Serial.print(fingerNames[i]);
    Serial.print("=");
    Serial.print((int)(fsrFilt[i] + 0.5f));
    if (i != FINGER_COUNT - 1) Serial.print(" ");
  }
  Serial.println();

  Serial.print("FSR_DELTA ");
  for (int i = 0; i < FINGER_COUNT; i++) {
    Serial.print(fingerNames[i]);
    Serial.print("=");
    Serial.print((int)(getFsrDeltaAdc(i, fsrFilt[i]) + 0.5f));
    if (i != FINGER_COUNT - 1) Serial.print(" ");
  }
  Serial.println();

  Serial.print("FSR_PCT ");
  for (int i = 0; i < FINGER_COUNT; i++) {
    Serial.print(fingerNames[i]);
    Serial.print("=");
    Serial.print((int)(fsrPct[i] + 0.5f));
    if (i != FINGER_COUNT - 1) Serial.print(" ");
  }
  Serial.println();

  Serial.print("FSR_N ");
  for (int i = 0; i < FINGER_COUNT; i++) {
    Serial.print(fingerNames[i]);
    Serial.print("=");
    Serial.print(fsrForceN[i], 2);
    if (i != FINGER_COUNT - 1) Serial.print(" ");
  }
  Serial.println();

  printPalmKeyReadback();
}

void maybeReportForceHits() {
  unsigned long now = millis();
  for (int i = 0; i < FINGER_COUNT; i++) {
    if (forceHitHoldUntil[i] != 0 && (long)(now - forceHitHoldUntil[i]) >= 0) {
      forceHitHoldUntil[i] = 0;
    }
  }
}

void updateServosNonBlocking() {
  unsigned long now = millis();
  if (now - lastServoUpdateMs < SERVO_UPDATE_MS) return;

  float dt = (now - lastServoUpdateMs) * 0.001f;
  if (lastServoUpdateMs == 0) dt = SERVO_UPDATE_MS * 0.001f;
  lastServoUpdateMs = now;

  updateAssistTargets();
  updateThumbDelayState();

  for (int i = 0; i < FINGER_COUNT; i++) {
    float tgt = targetCmd[i];
    tgt = computeSqueezeTarget(i, tgt);

    bool forceReached = false;
    if (forceLimitEnable) {
      if (forceLimitUseNewton) {
        forceReached = (fsrForceN[i] >= forceThresholdN[i]);
      } else {
        forceReached = (fsrPct[i] >= forceThresholdPct[i]);
      }
    }

    if (forceReached) {
      bool closing = isClosingTowardTarget(i, currentCmd[i], tgt);
      if (closing) {
        tgt = currentCmd[i];
        cancelAssist(i);
        if (forceHitHoldUntil[i] == 0) {
          if (forceLimitUseNewton) {
            Serial.print("FORCE_HIT_N ");
            Serial.print(fingerNames[i]);
            Serial.print(" F=");
            Serial.print(fsrForceN[i], 2);
            Serial.print(" T=");
            Serial.println(forceThresholdN[i], 2);
          } else {
            Serial.print("FORCE_HIT ");
            Serial.println(fingerNames[i]);
          }
        }
        forceHitHoldUntil[i] = millis() + FORCE_HIT_HOLD_MS;
      }
    }

    float step = maxDegPerSec[i] * dt;
    float cur = currentCmd[i];

    if (cur < tgt - step) cur += step;
    else if (cur > tgt + step) cur -= step;
    else cur = tgt;

    cur = clampFloat(cur, (float)SERVO_US_MIN, (float)SERVO_US_MAX);
    currentCmd[i] = cur;

    int out = (int)(cur + 0.5f);
    if (out != lastWritten[i]) {
      servos[i]->writeMicroseconds(out);
      lastWritten[i] = out;
    }
  }
}

void printReadback() {
  Serial.print("PINKY_US="); Serial.print((int)(currentCmd[IDX_PINKY] + 0.5f));
  Serial.print(" RING_US="); Serial.print((int)(currentCmd[IDX_RING] + 0.5f));
  Serial.print(" MIDDLE_US="); Serial.print((int)(currentCmd[IDX_MIDDLE] + 0.5f));
  Serial.print(" INDEX_US="); Serial.print((int)(currentCmd[IDX_INDEX] + 0.5f));
  Serial.print(" THUMB_US="); Serial.println((int)(currentCmd[IDX_THUMB] + 0.5f));
}

void printFsrReadback() {
  Serial.print("FSR_RAW ");
  for (int i = 0; i < FINGER_COUNT; i++) {
    Serial.print(fingerNames[i]);
    Serial.print("=");
    Serial.print((int)(fsrFilt[i] + 0.5f));
    if (i != FINGER_COUNT - 1) Serial.print(" ");
  }
  Serial.println();

  Serial.print("FSR_DELTA ");
  for (int i = 0; i < FINGER_COUNT; i++) {
    Serial.print(fingerNames[i]);
    Serial.print("=");
    Serial.print((int)(getFsrDeltaAdc(i, fsrFilt[i]) + 0.5f));
    if (i != FINGER_COUNT - 1) Serial.print(" ");
  }
  Serial.println();

  Serial.print("FSR_PCT ");
  for (int i = 0; i < FINGER_COUNT; i++) {
    Serial.print(fingerNames[i]);
    Serial.print("=");
    Serial.print((int)(fsrPct[i] + 0.5f));
    if (i != FINGER_COUNT - 1) Serial.print(" ");
  }
  Serial.println();

  Serial.print("FSR_N ");
  for (int i = 0; i < FINGER_COUNT; i++) {
    Serial.print(fingerNames[i]);
    Serial.print("=");
    Serial.print(fsrForceN[i], 2);
    if (i != FINGER_COUNT - 1) Serial.print(" ");
  }
  Serial.println();

  Serial.print("FSR_TARGET_N ");
  for (int i = 0; i < FINGER_COUNT; i++) {
    Serial.print(fingerNames[i]);
    Serial.print("=");
    Serial.print(forceThresholdN[i], 2);
    if (i != FINGER_COUNT - 1) Serial.print(" ");
  }
  Serial.println();

  printPalmKeyReadback();
}


void updateLoadcellNonBlocking() {
  if (!loadcellInitDone) return;

  unsigned long now = millis();
  if (now - lastLoadcellUpdateMs < LOADCELL_UPDATE_MS) return;
  lastLoadcellUpdateMs = now;

  if (loadcell.is_ready()) {
    loadcellRaw = loadcell.read();
    loadcellKg = loadcell.get_units(3);
    if (loadcellKg < 0.0f && loadcellKg > -0.03f) loadcellKg = 0.0f;
    loadcellN = loadcellKg * 9.81f;
  }
}

void printLoadcellReadback() {
  Serial.print("LOADCELL KG=");
  Serial.print(loadcellKg, 3);
  Serial.print(" N=");
  Serial.print(loadcellN, 3);
  Serial.print(" RAW=");
  Serial.println(loadcellRaw);
}

void maybeStreamLoadcell() {
  if (!loadcellStreamEnable) return;

  unsigned long now = millis();
  if (now - lastLoadcellStreamMs < LOADCELL_STREAM_MS) return;
  lastLoadcellStreamMs = now;

  printLoadcellReadback();
}

void printSqueezeConfig() {
  Serial.print("SQUEEZE=");
  Serial.println(squeezeAssistEnable ? "ON" : "OFF");
  for (int i = 0; i < FINGER_COUNT; i++) {
    Serial.print("SQUEEZE_CFG ");
    Serial.print(fingerNames[i]);
    Serial.print(" ENG=");
    Serial.print((int)(squeezeEngageClosePct[i] + 0.5f));
    Serial.print(" TOUCH=");
    Serial.print((int)(squeezeTouchPct[i] + 0.5f));
    Serial.print(" MAX=");
    Serial.print((int)(squeezeMaxExtraDeg[i] + 0.5f));
    Serial.print(" GAIN_X100=");
    Serial.println((int)(squeezeGainDegPerPct[i] * 100.0f + 0.5f));
  }
}

void commandAllOpen() {
  for (int i = 0; i < FINGER_COUNT; i++) {
    if (i == IDX_THUMB) setThumbTargetStaggered(openAngle[i]);
    else applySmartTarget(i, openAngle[i]);
  }
  Serial.println("OK OPEN ALL");
}

void commandAllClose() {
  for (int i = 0; i < FINGER_COUNT; i++) {
    if (i == IDX_THUMB) continue;
    applySmartTarget(i, closeAngle[i]);
  }
  setThumbTargetStaggered(closeAngle[IDX_THUMB]);
  Serial.println("OK CLOSE ALL");
}

void calibrateFsrOpen() {
  for (int i = 0; i < FINGER_COUNT; i++) {
    fsrOpenCal[i] = (int)(fsrFilt[i] + 0.5f);
  }
  Serial.println("OK CALFSR_OPEN");
  printFsrReadback();
}

void calibrateFsrClose() {
  for (int i = 0; i < FINGER_COUNT; i++) {
    int v = (int)(fsrFilt[i] + 0.5f);
    if (v <= fsrOpenCal[i] + 10) v = fsrOpenCal[i] + 10;
    fsrCloseCal[i] = v;
  }
  Serial.println("OK CALFSR_CLOSE");
  printFsrReadback();
}

void handleCommand(char* cmd) {
  trimInPlace(cmd);
  upperInPlace(cmd);

  if (strlen(cmd) == 0) return;

  if (strcmp(cmd, "OPEN") == 0) {
    commandAllOpen();
    return;
  }

  if (strcmp(cmd, "CLOSE") == 0) {
    commandAllClose();
    return;
  }

  if (strcmp(cmd, "READ") == 0) {
    printReadback();
    return;
  }

  if (strcmp(cmd, "READFSR") == 0) {
    printFsrReadback();
    return;
  }

  if (strcmp(cmd, "READPALM") == 0) {
    printPalmKeyReadback();
    return;
  }

  if (strncmp(cmd, "PALM_THR ", 9) == 0) {
    // PALM_THR command is acknowledged for compatibility.
    // Threshold is compile-time PALM_KEY_ADC_THRESHOLD in this version.
    Serial.println("OK PALM_THR USE_CODE_CONSTANT");
    return;
  }

  if (strncmp(cmd, "FSR_DB ", 7) == 0) {
    char* name = cmd + 7;
    char* sp = strchr(name, ' ');
    if (!sp) {
      Serial.print("ERR CMD: ");
      Serial.println(cmd);
      return;
    }
    *sp = '\0';
    char* valStr = sp + 1;
    trimInPlace(name);
    trimInPlace(valStr);
    int idx = getFingerIndex(name);
    if (idx < 0) {
      Serial.print("ERR FSR_DB FINGER: ");
      Serial.println(name);
      return;
    }
    int db = atoi(valStr);
    fsrForceDeadbandAdc[idx] = clampInt(db, 0, 1000);
    Serial.print("OK FSR_DB ");
    Serial.print(name);
    Serial.print("=");
    Serial.println(fsrForceDeadbandAdc[idx]);
    return;
  }

  if (strcmp(cmd, "READFSRDB") == 0) {
    Serial.print("FSR_DB ");
    for (int i = 0; i < FINGER_COUNT; i++) {
      Serial.print(fingerNames[i]);
      Serial.print("=");
      Serial.print(fsrForceDeadbandAdc[i]);
      if (i != FINGER_COUNT - 1) Serial.print(" ");
    }
    Serial.println();
    return;
  }


  if (strcmp(cmd, "READFORCE") == 0) {
    printFsrReadback();
    printLoadcellReadback();
    return;
  }

  if (strcmp(cmd, "READLOAD") == 0) {
    updateLoadcellNonBlocking();
    printLoadcellReadback();
    return;
  }

  if (strcmp(cmd, "TARELOAD") == 0) {
    if (loadcellInitDone && loadcell.is_ready()) {
      loadcell.tare(10);
      loadcellKg = 0.0f;
      loadcellN = 0.0f;
      Serial.println("OK TARELOAD");
    } else {
      Serial.println("ERR TARELOAD HX711_NOT_READY");
    }
    return;
  }

  if (strcmp(cmd, "LOAD_STREAM_ON") == 0) {
    loadcellStreamEnable = true;
    Serial.println("OK LOAD_STREAM_ON");
    return;
  }

  if (strcmp(cmd, "LOAD_STREAM_OFF") == 0) {
    loadcellStreamEnable = false;
    Serial.println("OK LOAD_STREAM_OFF");
    return;
  }

  if (strncmp(cmd, "LOADCAL ", 8) == 0) {
    float cal = atof(cmd + 8);
    if (fabs(cal) < 1.0f) {
      Serial.println("ERR LOADCAL");
      return;
    }
    loadcellCalibration = cal;
    if (loadcellInitDone) {
      loadcell.set_scale(loadcellCalibration);
    }
    Serial.print("OK LOADCAL=");
    Serial.println(loadcellCalibration, 3);
    return;
  }

  if (strcmp(cmd, "FSR_STREAM_ON") == 0) {
    fsrStreamEnable = true;
    Serial.println("OK FSR_STREAM_ON");
    return;
  }

  if (strcmp(cmd, "FSR_STREAM_OFF") == 0) {
    fsrStreamEnable = false;
    Serial.println("OK FSR_STREAM_OFF");
    return;
  }

  if (strcmp(cmd, "CALFSR_OPEN") == 0) {
    calibrateFsrOpen();
    return;
  }

  if (strcmp(cmd, "CALFSR_CLOSE") == 0) {
    calibrateFsrClose();
    return;
  }

  if (strcmp(cmd, "FORCE_ON") == 0) {
    forceLimitEnable = true;
    Serial.println("OK FORCE_ON");
    return;
  }

  if (strcmp(cmd, "FORCE_OFF") == 0) {
    forceLimitEnable = false;
    Serial.println("OK FORCE_OFF");
    return;
  }

  if (strcmp(cmd, "READSQUEEZE") == 0) {
    printSqueezeConfig();
    return;
  }

  if (strcmp(cmd, "SQUEEZE_ON") == 0) {
    squeezeAssistEnable = true;
    Serial.println("OK SQUEEZE_ON");
    return;
  }

  if (strcmp(cmd, "SQUEEZE_OFF") == 0) {
    squeezeAssistEnable = false;
    Serial.println("OK SQUEEZE_OFF");
    return;
  }

  if (strcmp(cmd, "THUMB_DELAY_ON") == 0) {
    thumbDelayEnable = true;
    Serial.println("OK THUMB_DELAY_ON");
    return;
  }

  if (strcmp(cmd, "THUMB_DELAY_OFF") == 0) {
    thumbDelayEnable = false;
    cancelThumbDelay();
    Serial.println("OK THUMB_DELAY_OFF");
    return;
  }

  if (strncmp(cmd, "THUMB_DELAY_MS ", 15) == 0) {
    int ms = atoi(cmd + 15);
    thumbCloseDelayMs = (unsigned long)clampInt(ms, 0, 5000);
    Serial.print("OK THUMB_DELAY_MS=");
    Serial.println((int)thumbCloseDelayMs);
    return;
  }

  if (strncmp(cmd, "FORCE_THR ", 10) == 0) {
    char* name = cmd + 10;
    char* sp = strchr(name, ' ');
    if (!sp) {
      Serial.print("ERR CMD: ");
      Serial.println(cmd);
      return;
    }
    *sp = '\0';
    char* valStr = sp + 1;
    trimInPlace(name);
    trimInPlace(valStr);
    int idx = getFingerIndex(name);
    if (idx < 0) {
      Serial.print("ERR CMD: ");
      Serial.println(cmd);
      return;
    }
    float thr = atof(valStr);
    forceLimitUseNewton = false;
    forceThresholdPct[idx] = clampFloat(thr, 0.0f, 100.0f);
    Serial.print("OK FORCE_THR ");
    Serial.print(name);
    Serial.print("=");
    Serial.println((int)(forceThresholdPct[idx] + 0.5f));
    return;
  }

  if (strncmp(cmd, "FORCE_THR_N ", 12) == 0) {
    char* name = cmd + 12;
    char* sp = strchr(name, ' ');
    if (!sp) {
      Serial.print("ERR CMD: ");
      Serial.println(cmd);
      return;
    }
    *sp = '\0';
    char* valStr = sp + 1;
    trimInPlace(name);
    trimInPlace(valStr);
    int idx = getFingerIndex(name);
    if (idx < 0) {
      Serial.print("ERR CMD: ");
      Serial.println(cmd);
      return;
    }
    float thrN = atof(valStr);
    forceLimitUseNewton = true;
    forceThresholdN[idx] = clampFloat(thrN, 0.0f, 30.0f);
    Serial.print("OK FORCE_THR_N ");
    Serial.print(name);
    Serial.print("=");
    Serial.println(forceThresholdN[idx], 2);
    return;
  }

  if (strcmp(cmd, "FORCE_MODE_N") == 0) {
    forceLimitUseNewton = true;
    Serial.println("OK FORCE_MODE_N");
    return;
  }

  if (strcmp(cmd, "FORCE_MODE_PCT") == 0) {
    forceLimitUseNewton = false;
    Serial.println("OK FORCE_MODE_PCT");
    return;
  }

  if (strncmp(cmd, "SQUEEZE_TOUCH ", 14) == 0) {
    char* name = cmd + 14;
    char* sp = strchr(name, ' ');
    if (!sp) {
      Serial.print("ERR CMD: ");
      Serial.println(cmd);
      return;
    }
    *sp = '\0';
    char* valStr = sp + 1;
    trimInPlace(name);
    trimInPlace(valStr);
    int idx = getFingerIndex(name);
    if (idx < 0) {
      Serial.print("ERR CMD: ");
      Serial.println(cmd);
      return;
    }
    float v = atof(valStr);
    squeezeTouchPct[idx] = clampFloat(v, 0.0f, 100.0f);
    Serial.print("OK SQUEEZE_TOUCH ");
    Serial.print(name);
    Serial.print("=");
    Serial.println((int)(squeezeTouchPct[idx] + 0.5f));
    return;
  }

  if (strncmp(cmd, "SQUEEZE_MAX ", 12) == 0) {
    char* name = cmd + 12;
    char* sp = strchr(name, ' ');
    if (!sp) {
      Serial.print("ERR CMD: ");
      Serial.println(cmd);
      return;
    }
    *sp = '\0';
    char* valStr = sp + 1;
    trimInPlace(name);
    trimInPlace(valStr);
    int idx = getFingerIndex(name);
    if (idx < 0) {
      Serial.print("ERR CMD: ");
      Serial.println(cmd);
      return;
    }
    float v = atof(valStr);
    squeezeMaxExtraDeg[idx] = clampFloat(v, 0.0f, 45.0f);
    Serial.print("OK SQUEEZE_MAX ");
    Serial.print(name);
    Serial.print("=");
    Serial.println((int)(squeezeMaxExtraDeg[idx] + 0.5f));
    return;
  }

  if (strncmp(cmd, "SQUEEZE_GAIN ", 13) == 0) {
    char* name = cmd + 13;
    char* sp = strchr(name, ' ');
    if (!sp) {
      Serial.print("ERR CMD: ");
      Serial.println(cmd);
      return;
    }
    *sp = '\0';
    char* valStr = sp + 1;
    trimInPlace(name);
    trimInPlace(valStr);
    int idx = getFingerIndex(name);
    if (idx < 0) {
      Serial.print("ERR CMD: ");
      Serial.println(cmd);
      return;
    }
    float v = atof(valStr);
    squeezeGainDegPerPct[idx] = clampFloat(v, 0.0f, 5.0f);
    Serial.print("OK SQUEEZE_GAIN ");
    Serial.print(name);
    Serial.print("=");
    Serial.println((int)(squeezeGainDegPerPct[idx] * 100.0f + 0.5f));
    return;
  }

  if (strncmp(cmd, "SQUEEZE_ENGAGE ", 15) == 0) {
    char* name = cmd + 15;
    char* sp = strchr(name, ' ');
    if (!sp) {
      Serial.print("ERR CMD: ");
      Serial.println(cmd);
      return;
    }
    *sp = '\0';
    char* valStr = sp + 1;
    trimInPlace(name);
    trimInPlace(valStr);
    int idx = getFingerIndex(name);
    if (idx < 0) {
      Serial.print("ERR CMD: ");
      Serial.println(cmd);
      return;
    }
    float v = atof(valStr);
    squeezeEngageClosePct[idx] = clampFloat(v, 0.0f, 100.0f);
    Serial.print("OK SQUEEZE_ENGAGE ");
    Serial.print(name);
    Serial.print("=");
    Serial.println((int)(squeezeEngageClosePct[idx] + 0.5f));
    return;
  }

  char* sp = strchr(cmd, ' ');
  if (!sp) {
    Serial.print("ERR CMD: ");
    Serial.println(cmd);
    return;
  }

  *sp = '\0';
  char* name = cmd;
  char* valStr = sp + 1;
  trimInPlace(name);
  trimInPlace(valStr);

  bool pctMode = false;
  if (endsWith(name, "_PCT")) {
    pctMode = true;
    name[strlen(name) - 4] = '\0';
  }

  int idx = getFingerIndex(name);
  if (idx < 0) {
    Serial.print("ERR CMD: ");
    Serial.print(name);
    Serial.print(" ");
    Serial.println(valStr);
    return;
  }

  int val = atoi(valStr);
  int ang = pctMode ? pctToAngle(val, openAngle[idx], closeAngle[idx])
                    : clampInt(val, SERVO_US_MIN, SERVO_US_MAX);

  if (idx == IDX_THUMB) setThumbTargetStaggered(ang);
  else applySmartTarget(idx, ang);

  Serial.print("OK ");
  Serial.print(name);
  if (pctMode) Serial.print("_PCT");
  Serial.print(" TGT_US=");
  Serial.println((int)(targetCmd[idx] + 0.5f));
}

void readSerialNonBlocking() {
  while (Serial.available()) {
    char c = (char)Serial.read();
    if (c == '\n' || c == '\r') {
      if (rxLen > 0) {
        rxBuf[rxLen] = '\0';
        handleCommand(rxBuf);
        rxLen = 0;
      }
    } else {
      if (rxLen < sizeof(rxBuf) - 1) {
        rxBuf[rxLen++] = c;
      } else {
        rxLen = 0;
      }
    }
  }
}

void setup() {
  Serial.begin(115200);
  delay(300);

  analogReadResolution(12);
  analogSetAttenuation(ADC_11db);

  loadcell.begin(LOADCELL_DT_PIN, LOADCELL_SCK_PIN);
  loadcell.set_scale(loadcellCalibration);

  unsigned long hxStart = millis();
  while (!loadcell.is_ready() && (millis() - hxStart < 1200)) {
    delay(10);
  }

  if (loadcell.is_ready()) {
    loadcell.tare(10);
    loadcellInitDone = true;
    Serial.println("HX711_READY");
  } else {
    loadcellInitDone = false;
    Serial.println("HX711_NOT_READY");
  }

  for (int i = 0; i < FINGER_COUNT; i++) {
    pinMode(fsrPins[i], INPUT);
  }
  pinMode(PALM_KEY_PIN, INPUT);

  for (int i = 0; i < FINGER_COUNT; i++) {
    servos[i]->setPeriodHertz(50);
    servos[i]->attach(servoPins[i], SERVO_US_MIN, SERVO_US_MAX);
    currentCmd[i] = (float)openAngle[i];
    targetCmd[i] = (float)openAngle[i];
    lastWritten[i] = openAngle[i];
    servos[i]->writeMicroseconds(openAngle[i]);
  }

  // V10: ep mo tat ca ngon trong luc khoi dong de tranh servo ve vi tri mac dinh 90 do.
  unsigned long bootOpenStartMs = millis();
  while (millis() - bootOpenStartMs < 1800) {
    for (int i = 0; i < FINGER_COUNT; i++) {
      servos[i]->writeMicroseconds(openAngle[i]);
      currentCmd[i] = (float)openAngle[i];
      targetCmd[i] = (float)openAngle[i];
      lastWritten[i] = openAngle[i];
    }
    delay(20);
  }

  for (int k = 0; k < 20; k++) {
    updateFsrNonBlocking();
    updatePalmKeyNonBlocking();
    delay(5);
  }

  thumbDelayedTarget = (float)openAngle[IDX_THUMB];

  lastServoUpdateMs = millis();
  lastFsrUpdateMs = millis();
  lastFsrStreamMs = millis();
  lastLoadcellUpdateMs = millis();
  lastLoadcellStreamMs = millis();

  Serial.println("ALL5_READY_DATN_V56_THUMB_DYNAMIC_FORCE_FIX");
  printReadback();
  printFsrReadback();
  printPalmKeyReadback();
  printLoadcellReadback();
  printSqueezeConfig();
}

void loop() {
  readSerialNonBlocking();
  updateFsrNonBlocking();
  updatePalmKeyNonBlocking();
  updateLoadcellNonBlocking();
  updateServosNonBlocking();
  maybeStreamFsr();
  maybeStreamLoadcell();
  maybeReportForceHits();
}
