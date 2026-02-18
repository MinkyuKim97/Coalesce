#include <Wire.h>
#include <Adafruit_MPU6050.h>
#include <Adafruit_Sensor.h>

// -------------------- I2C PINS (ESP32) --------------------
#define I2C_SDA 32
#define I2C_SCL 25

Adafruit_MPU6050 mpu;

// -------------------- TIMING --------------------
static const uint32_t SAMPLE_MS = 20;   // 50 Hz sampling

// -------------------- FILTERING --------------------
static const float GRAV_ALPHA  = 0.02f; // slow gravity estimate
static const float MOVE_ALPHA  = 0.4f;  // fast gesture response

// -------------------- HANDSHAKE PARAMETERS --------------------
static const uint32_t HANDSHAKE_MIN_MS = 300;
static const uint32_t HANDSHAKE_MAX_MS = 1500;

static const float SHAKE_HIGH = 0.6f;   // oscillation peak
static const float SHAKE_LOW  = 0.25f;  // oscillation valley

static const uint8_t MIN_SHAKES = 3;
static const uint8_t MAX_SHAKES = 8;

// -------------------- GRAVITY ESTIMATE --------------------
static float gx = 0, gy = 0, gz = 0;

// -------------------- HANDSHAKE STATE --------------------
static bool shaking = false;
static bool shakeArmed = true;

static uint32_t shakeStartMs = 0;
static uint8_t shakeCount = 0;
static uint32_t lastSampleMs = 0;

// -------------------- CALLBACK --------------------
void onHandshakeDetected() {
  Serial.println("Handshake detected");
  // confirms handshake has occured
}

// -------------------- SETUP --------------------
void setup() {
  Serial.begin(115200);

  pinMode(I2C_SDA, INPUT_PULLUP);
  pinMode(I2C_SCL, INPUT_PULLUP);

  Wire.begin(I2C_SDA, I2C_SCL);
  Wire.setClock(100000);
  delay(300);

  if (!mpu.begin(0x68, &Wire)) {
    Serial.println("MPU6050 not found!");
    while (true) delay(10);
  }

  mpu.setAccelerometerRange(MPU6050_RANGE_8_G);
  mpu.setGyroRange(MPU6050_RANGE_500_DEG);
  mpu.setFilterBandwidth(MPU6050_BAND_21_HZ);

  Serial.println("MPU6050 ready for handshake detection.");
}

// -------------------- LOOP --------------------
void loop() {
  uint32_t now = millis();
  if (now - lastSampleMs < SAMPLE_MS) return;
  lastSampleMs = now;

  // -------- Read IMU --------
  sensors_event_t a, g, t;
  mpu.getEvent(&a, &g, &t);

  float ax = a.acceleration.x;
  float ay = a.acceleration.y;
  float az = a.acceleration.z;

  // -------- Gravity estimation (low-pass) --------
  gx = gx * (1.0f - GRAV_ALPHA) + ax * GRAV_ALPHA;
  gy = gy * (1.0f - GRAV_ALPHA) + ay * GRAV_ALPHA;
  gz = gz * (1.0f - GRAV_ALPHA) + az * GRAV_ALPHA;

  // -------- Linear acceleration --------
  float lx = ax - gx;
  float ly = ay - gy;
  float lz = az - gz;

  // -------- Horizontal motion magnitude --------
  float horiz = sqrtf(lx * lx + ly * ly);

  // -------- Fast EMA for gesture response --------
  static float horizEma = 0;
  horizEma = horizEma * (1.0f - MOVE_ALPHA) + horiz * MOVE_ALPHA;

  // -------- Handshake start --------
  if (!shaking && horizEma > SHAKE_HIGH) {
    shaking = true;
    shakeStartMs = now;
    shakeCount = 0;
    shakeArmed = true;
  }

  // -------- Oscillation counting --------
  if (shaking) {
    if (shakeArmed && horizEma > SHAKE_HIGH) {
      shakeCount++;
      shakeArmed = false;
    }

    if (!shakeArmed && horizEma < SHAKE_LOW) {
      shakeArmed = true;
    }
  }

  // -------- Handshake end --------
  if (shaking && horizEma < SHAKE_LOW) {
    uint32_t duration = now - shakeStartMs;

    if (duration >= HANDSHAKE_MIN_MS &&
        duration <= HANDSHAKE_MAX_MS &&
        shakeCount >= MIN_SHAKES &&
        shakeCount <= MAX_SHAKES) {
      onHandshakeDetected();
    }

    // Reset state
    shaking = false;
    shakeCount = 0;
    shakeArmed = true;
  }
}