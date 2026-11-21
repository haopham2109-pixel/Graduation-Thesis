#include "HX711.h"
#include <math.h>

#define HX711_DT   19
#define HX711_SCK  18

HX711 scale;

// He so calib hien tai cua ban
// Neu can theo iPhone 11 Pro Max khong op thi tam dung 5094.0
float calibration_factor = 5094.0;

#define SAMPLE_NUM 60
#define ZERO_DEADBAND_G 5.0

long zero_raw = 0;
float last_good_weight = 0;

long readMedianRaw() {
  long arr[SAMPLE_NUM];
  int count = 0;

  for (int i = 0; i < SAMPLE_NUM; i++) {
    if (scale.wait_ready_timeout(300)) {
      long raw = scale.read();

      // Loai mau rac thuong gap
      if (raw != -1 && raw != 0 && raw > -8000000 && raw < 8000000) {
        arr[count++] = raw;
      }
    }

    delay(10);
  }

  if (count < 10) {
    return zero_raw;
  }

  // Sap xep de lay median
  for (int i = 0; i < count - 1; i++) {
    for (int j = i + 1; j < count; j++) {
      if (arr[j] < arr[i]) {
        long temp = arr[i];
        arr[i] = arr[j];
        arr[j] = temp;
      }
    }
  }

  return arr[count / 2];
}

void doTare() {
  Serial.println();
  Serial.println("Bo het vat ra khoi can.");
  Serial.println("Cho HX711 va loadcell on dinh 10 giay...");
  delay(10000);

  Serial.println("Dang tare...");

  long sum = 0;
  int n = 10;

  for (int i = 0; i < n; i++) {
    sum += readMedianRaw();
    delay(100);
  }

  zero_raw = sum / n;

  last_good_weight = 0;

  Serial.print("ZERO RAW = ");
  Serial.println(zero_raw);
  Serial.println("Tare xong. Dat vat len can.");
  Serial.println();
}

void setup() {
  Serial.begin(115200);
  delay(1000);

  Serial.println();
  Serial.println("===== CAN HX711 LOC SPIKE - GPIO19/18 =====");

  pinMode(HX711_SCK, OUTPUT);
  digitalWrite(HX711_SCK, LOW);

  scale.begin(HX711_DT, HX711_SCK);

  if (!scale.wait_ready_timeout(3000)) {
    Serial.println("LOI: HX711 khong san sang!");
    Serial.println("Kiem tra VCC, GND, DT, SCK.");
    while (1);
  }

  Serial.println("HX711 OK.");
  Serial.println("Nhap t de tare lai.");

  doTare();
}

void loop() {
  if (Serial.available()) {
    char c = Serial.read();

    if (c == 't' || c == 'T') {
      doTare();
    }
  }

  long raw = readMedianRaw();
  long delta = raw - zero_raw;

  float weight = (float)delta / calibration_factor;

  // Bo qua spike bat thuong
  if (abs(weight - last_good_weight) > 80.0 && abs(last_good_weight) > 1.0) {
    Serial.print("BO QUA SPIKE | RAW = ");
    Serial.print(raw);
    Serial.print(" | weight rac = ");
    Serial.print(weight, 2);
    Serial.println(" g");

    weight = last_good_weight;
  } else {
    last_good_weight = weight;
  }

  if (weight > -ZERO_DEADBAND_G && weight < ZERO_DEADBAND_G) {
    weight = 0;
  }

  Serial.print("RAW = ");
  Serial.print(raw);
  Serial.print(" | DELTA = ");
  Serial.print(delta);
  Serial.print(" | Khoi luong = ");
  Serial.print(weight, 2);
  Serial.println(" g");

  delay(700);
}
