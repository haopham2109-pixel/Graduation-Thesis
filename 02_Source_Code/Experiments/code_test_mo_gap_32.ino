#include <ESP32Servo.h>

// ============================================================
// 5 SERVO MICROSECOND TEST
// Lenh vi du:
//   US 500 MI
//   US 2100 MI
//   US 400 RI
//   US 1300 THU
//   US 2300 IN
//   READ
//   OPEN
//   CLOSE
//   SCAN MI
// ============================================================

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
  32,  // PINKY
  33,  // RING
  25,  // MIDDLE
  26,  // INDEX
  27   // THUMB
};

// Gioi han an toan cua servo
const int SERVO_US_MIN = 400;
const int SERVO_US_MAX = 2500;

// Xung mo hien tai ban dang muon test
int openUs[FINGER_COUNT] = {
  2100,  // PINKY
  2100,  // RING
  400,   // MIDDLE
  400,   // INDEX
  400    // THUMB
};

// Xung gap hien tai theo code chinh
int closeUs[FINGER_COUNT] = {
  400,   // PINKY
  400,   // RING
  2100,  // MIDDLE
  2300,  // INDEX
  1300   // THUMB
};

int currentUs[FINGER_COUNT];

String readLine() {
  String cmd = Serial.readStringUntil('\n');
  cmd.trim();
  cmd.toUpperCase();
  return cmd;
}

int clampUs(int us) {
  if (us < SERVO_US_MIN) us = SERVO_US_MIN;
  if (us > SERVO_US_MAX) us = SERVO_US_MAX;
  return us;
}

int getFingerIndex(String s) {
  s.trim();
  s.toUpperCase();

  if (s == "PI" || s == "PINKY" || s == "UT" || s == "P") return IDX_PINKY;
  if (s == "RI" || s == "RING" || s == "APUT" || s == "R") return IDX_RING;
  if (s == "MI" || s == "MIDDLE" || s == "GIUA" || s == "M") return IDX_MIDDLE;
  if (s == "IN" || s == "INDEX" || s == "TRO" || s == "I") return IDX_INDEX;
  if (s == "TH" || s == "THU" || s == "THUMB" || s == "CAI" || s == "T") return IDX_THUMB;

  return -1;
}

void writeFingerUs(int idx, int us) {
  if (idx < 0 || idx >= FINGER_COUNT) return;

  us = clampUs(us);
  currentUs[idx] = us;
  servos[idx]->writeMicroseconds(us);

  Serial.print("OK ");
  Serial.print(fingerNames[idx]);
  Serial.print(" US=");
  Serial.println(us);
}

void printReadback() {
  Serial.println("===== READ CURRENT US =====");
  for (int i = 0; i < FINGER_COUNT; i++) {
    Serial.print(fingerNames[i]);
    Serial.print("_US=");
    Serial.print(currentUs[i]);
    Serial.print("  OPEN=");
    Serial.print(openUs[i]);
    Serial.print("  CLOSE=");
    Serial.println(closeUs[i]);
  }
}

void openAll() {
  Serial.println("OPEN ALL");
  for (int i = 0; i < FINGER_COUNT; i++) {
    writeFingerUs(i, openUs[i]);
    delay(120);
  }
}

void closeAll() {
  Serial.println("CLOSE ALL");
  for (int i = 0; i < FINGER_COUNT; i++) {
    writeFingerUs(i, closeUs[i]);
    delay(120);
  }
}

void scanFinger(int idx) {
  if (idx < 0 || idx >= FINGER_COUNT) {
    Serial.println("ERR SCAN FINGER");
    return;
  }

  Serial.print("SCAN ");
  Serial.println(fingerNames[idx]);

  Serial.println("SCAN 400 -> 2500");
  for (int us = SERVO_US_MIN; us <= SERVO_US_MAX; us += 100) {
    writeFingerUs(idx, us);
    delay(700);
  }

  Serial.println("SCAN 2500 -> 400");
  for (int us = SERVO_US_MAX; us >= SERVO_US_MIN; us -= 100) {
    writeFingerUs(idx, us);
    delay(700);
  }

  Serial.println("DONE SCAN");
}

void printHelp() {
  Serial.println();
  Serial.println("=== 5 SERVO MICROSECOND TEST ===");
  Serial.println("Lenh dieu khien tung ngon:");
  Serial.println("  US 500 MI     -> MIDDLE ve 500us");
  Serial.println("  US 2100 MI    -> MIDDLE ve 2100us");
  Serial.println("  US 400 RI     -> RING ve 400us");
  Serial.println("  US 2300 IN    -> INDEX ve 2300us");
  Serial.println("  US 1300 THU   -> THUMB ve 1300us");
  Serial.println();
  Serial.println("Ten ngon ho tro:");
  Serial.println("  PI/PINKY   = ngon ut");
  Serial.println("  RI/RING    = ngon ap ut");
  Serial.println("  MI/MIDDLE  = ngon giua");
  Serial.println("  IN/INDEX   = ngon tro");
  Serial.println("  THU/THUMB  = ngon cai");
  Serial.println();
  Serial.println("Lenh khac:");
  Serial.println("  READ");
  Serial.println("  OPEN");
  Serial.println("  CLOSE");
  Serial.println("  SCAN MI");
  Serial.println("  SCAN RI");
  Serial.println("  HELP");
  Serial.println();
}

void handleCommand(String cmd) {
  if (cmd.length() == 0) return;

  if (cmd == "HELP") {
    printHelp();
    return;
  }

  if (cmd == "READ") {
    printReadback();
    return;
  }

  if (cmd == "OPEN") {
    openAll();
    return;
  }

  if (cmd == "CLOSE") {
    closeAll();
    return;
  }

  if (cmd.startsWith("SCAN ")) {
    String f = cmd.substring(5);
    int idx = getFingerIndex(f);
    if (idx < 0) {
      Serial.println("ERR FINGER. Vi du: SCAN MI");
      return;
    }
    scanFinger(idx);
    return;
  }

  // Dang ho tro cu phap: US 500 MI
  if (cmd.startsWith("US ")) {
    int firstSpace = cmd.indexOf(' ');
    int secondSpace = cmd.indexOf(' ', firstSpace + 1);

    if (secondSpace < 0) {
      Serial.println("ERR. Dung cu phap: US 500 MI");
      return;
    }

    String usStr = cmd.substring(firstSpace + 1, secondSpace);
    String fingerStr = cmd.substring(secondSpace + 1);

    int us = usStr.toInt();
    int idx = getFingerIndex(fingerStr);

    if (idx < 0) {
      Serial.println("ERR FINGER. Vi du: US 500 MI / RI / IN / THU / PI");
      return;
    }

    writeFingerUs(idx, us);
    return;
  }

  Serial.println("Lenh sai. Go HELP de xem lenh.");
}

void setup() {
  Serial.begin(115200);
  delay(1000);

  printHelp();

  for (int i = 0; i < FINGER_COUNT; i++) {
    servos[i]->setPeriodHertz(50);
    servos[i]->attach(servoPins[i], SERVO_US_MIN, SERVO_US_MAX);

    currentUs[i] = clampUs(openUs[i]);
    servos[i]->writeMicroseconds(currentUs[i]);
    delay(120);
  }

  Serial.println("ALL 5 SERVO ATTACHED.");
  Serial.println("Da dua tat ca ve openUs.");
  printReadback();
}

void loop() {
  if (Serial.available()) {
    String cmd = readLine();
    handleCommand(cmd);
  }
}
