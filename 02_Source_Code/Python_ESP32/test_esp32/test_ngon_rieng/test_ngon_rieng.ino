#include <ESP32Servo.h>

Servo testServo;

// DOI CHAN O DAY DE TEST TUNG NGON
// PINKY  = 32
// RING   = 33
// MIDDLE = 25
// INDEX  = 26
// THUMB  = 27
const int SERVO_PIN = 27;   // dang test INDEX

int currentUs = 1500;

void setup() {
  Serial.begin(115200);
  delay(1000);

  Serial.println();
  Serial.println("=== SINGLE SERVO MICROSECOND TEST ===");
  Serial.println("Dang test 1 servo duy nhat.");
  Serial.println("Nhap lenh:");
  Serial.println("  US 500");
  Serial.println("  US 1000");
  Serial.println("  US 1500");
  Serial.println("  US 2000");
  Serial.println("  US 2500");
  Serial.println("  OPEN_SCAN");
  Serial.println();

  testServo.setPeriodHertz(50);
  testServo.attach(SERVO_PIN, 500, 2500);

  delay(300);

  // vi tri trung gian de xem servo co phan ung khong
  testServo.writeMicroseconds(currentUs);
  Serial.print("START US=");
  Serial.println(currentUs);
}

void loop() {
  if (Serial.available()) {
    String cmd = Serial.readStringUntil('\n');
    cmd.trim();
    cmd.toUpperCase();

    if (cmd.startsWith("US ")) {
      int us = cmd.substring(3).toInt();
      us = constrain(us, 500, 2500);
      currentUs = us;

      testServo.writeMicroseconds(currentUs);

      Serial.print("OK US=");
      Serial.println(currentUs);
    }

    else if (cmd == "OPEN_SCAN") {
      Serial.println("SCAN 500 -> 2500");
      for (int us = 500; us <= 2500; us += 100) {
        testServo.writeMicroseconds(us);
        Serial.print("US=");
        Serial.println(us);
        delay(900);
      }

      Serial.println("SCAN 2500 -> 500");
      for (int us = 2500; us >= 500; us -= 100) {
        testServo.writeMicroseconds(us);
        Serial.print("US=");
        Serial.println(us);
        delay(900);
      }

      Serial.println("DONE SCAN");
    }

    else {
      Serial.println("Lenh sai. Vi du: US 1500 hoac OPEN_SCAN");
    }
  }
}
