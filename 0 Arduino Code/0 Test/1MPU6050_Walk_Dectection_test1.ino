#include <Wire.h>
#include <Adafruit_MPU6050.h>
#include <Adafruit_Sensor.h>
#include <Adafruit_CircuitPlayground.h>

Adafruit_MPU6050 mpu;

// -------------------- TIMING --------------------
static const uint32_t SAMPLE_MS = 20;   // 50 Hz sampling

// -------------------- FILTERING --------------------
static const float GRAV_ALPHA  = 0.1f;  // FASTER gravity estimate (was 0.02)
static const float MOVE_ALPHA  = 0.6f;  // FASTER gesture response (was 0.4)

// -------------------- HANDSHAKE PARAMETERS --------------------
// ⚙️ ADJUST THESE VALUES TO CHANGE SENSITIVITY ⚙️

// Motion threshold to be considered "shaking"
static const float MOTION_THRESHOLD = 1.5f;  // m/s² of vertical motion

// How long motion must be sustained to count as handshake
static const uint32_t MIN_SHAKE_DURATION_MS = 200;  // 200ms minimum

// How long motion must stop before ending detection
static const uint32_t MOTION_TIMEOUT_MS = 150;  // 150ms of no motion = done

// Cooldown between detections
static const uint32_t COOLDOWN_MS = 500;  // 0.5 second cooldown

// -------------------- GRAVITY ESTIMATE --------------------
static float gx = 0, gy = 0, gz = 0;

// -------------------- HANDSHAKE STATE --------------------
static bool motionActive = false;
static uint32_t motionStartMs = 0;
static uint32_t lastMotionMs = 0;
static uint32_t lastDetectionMs = 0;
static uint32_t lastSampleMs = 0;

// -------------------- HANDSHAKE COUNTER --------------------
static uint32_t totalHandshakes = 0;

// -------------------- CALLBACK --------------------
void onHandshakeDetected() {
  totalHandshakes++;
  
  Serial.print("🤝 Handshake #");
  Serial.print(totalHandshakes);
  Serial.print(" detected!");
  
  CircuitPlayground.setPixelColor(0, 0, 255, 0); // Green light
  CircuitPlayground.playTone(1000, 100); // Beep
  delay(100);
  CircuitPlayground.clearPixels();
}

// -------------------- SETUP --------------------
void setup() {
  Serial.begin(115200);
  while (!Serial) delay(10);
  
  CircuitPlayground.begin();
  Wire.begin();
  
  delay(300);
  
  Serial.println("=================================");
  Serial.println("MPU6050 Handshake Detector v2");
  Serial.println("=================================");
  
  if (!mpu.begin(0x68, &Wire)) {
    Serial.println("❌ MPU6050 not found!");
    Serial.println("Check wiring:");
    Serial.println("  MPU VCC → 3.3V");
    Serial.println("  MPU GND → GND");
    Serial.println("  MPU SCL → SCL pad");
    Serial.println("  MPU SDA → SDA pad");
    
    while (true) {
      CircuitPlayground.setPixelColor(0, 255, 0, 0);
      delay(500);
      CircuitPlayground.clearPixels();
      delay(500);
    }
  }
  
  mpu.setAccelerometerRange(MPU6050_RANGE_8_G);
  mpu.setGyroRange(MPU6050_RANGE_500_DEG);
  mpu.setFilterBandwidth(MPU6050_BAND_21_HZ);
  
  Serial.println("✅ MPU6050 connected!");
  Serial.println("Shake the MPU up and down continuously!");
  Serial.print("Motion threshold: ");
  Serial.print(MOTION_THRESHOLD);
  Serial.println(" m/s²");
  Serial.print("Min duration: ");
  Serial.print(MIN_SHAKE_DURATION_MS);
  Serial.println(" ms");
  Serial.print("Timeout: ");
  Serial.print(MOTION_TIMEOUT_MS);
  Serial.println(" ms");
  Serial.println("=================================");
  
  CircuitPlayground.setPixelColor(0, 0, 0, 255); // Blue = ready
  delay(1000);
  CircuitPlayground.clearPixels();
}

// -------------------- LOOP --------------------
// void loop() {
//   uint32_t now = millis();
//   if (now - lastSampleMs < SAMPLE_MS) return;
//   lastSampleMs = now;
  
//   // -------- Read MPU6050 --------
//   sensors_event_t a, g, t;
//   mpu.getEvent(&a, &g, &t);
  
//   float ax = a.acceleration.x;
//   float ay = a.acceleration.y;
//   float az = a.acceleration.z;
  
//   // -------- FASTER Gravity estimation --------
//   gx = gx * (1.0f - GRAV_ALPHA) + ax * GRAV_ALPHA;
//   gy = gy * (1.0f - GRAV_ALPHA) + ay * GRAV_ALPHA;
//   gz = gz * (1.0f - GRAV_ALPHA) + az * GRAV_ALPHA;
  
//   // -------- Linear acceleration --------
//   float lx = ax - gx;
//   float ly = ay - gy;
//   float lz = az - gz;
  
//   // -------- VERTICAL motion magnitude --------
//   float vertical = fabsf(lz);
  
//   // -------- FASTER EMA --------
//   static float vertEma = 0;
//   vertEma = vertEma * (1.0f - MOVE_ALPHA) + vertical * MOVE_ALPHA;
  
//   // 🔍 UNCOMMENT TO SEE LIVE VALUES
//   // Serial.print("Motion: "); Serial.print(vertEma); 
//   // Serial.print(" | Active: "); Serial.println(motionActive);
  
//   // -------- DETECT SUSTAINED MOTION --------
  
//   // If motion exceeds threshold
//   if (vertEma > MOTION_THRESHOLD) {
//     lastMotionMs = now;  // Update last motion time
    
//     // Start tracking if not already
//     if (!motionActive) {
//       motionActive = true;
//       motionStartMs = now;
//       Serial.println("📈 Motion started");
//     }
//   }
  
//   // -------- CHECK IF MOTION HAS STOPPED --------
//   if (motionActive) {
//     uint32_t timeSinceMotion = now - lastMotionMs;
    
//     // If no motion for MOTION_TIMEOUT_MS, end the shake
//     if (timeSinceMotion > MOTION_TIMEOUT_MS) {
//       uint32_t shakeDuration = lastMotionMs - motionStartMs;
//       uint32_t timeSinceLastDetection = now - lastDetectionMs;
      
//       Serial.print("🛑 Motion stopped. Duration: ");
//       Serial.print(shakeDuration);
//       Serial.println(" ms");
      
//       // Check if it was sustained long enough AND cooldown passed
//       if (shakeDuration >= MIN_SHAKE_DURATION_MS && 
//           timeSinceLastDetection > COOLDOWN_MS) {
//         onHandshakeDetected();
//         lastDetectionMs = now;
//       } else if (shakeDuration < MIN_SHAKE_DURATION_MS) {
//         Serial.println("❌ Too short - not a handshake");
//       } else {
//         Serial.println("⏳ Cooldown active");
//       }
      
//       // Reset motion tracking
//       motionActive = false;
//     }
//   }
// }

// -------------------- LOOP --------------------
void loop() {
  uint32_t now = millis();
  if (now - lastSampleMs < SAMPLE_MS) return;
  lastSampleMs = now;
  
  // -------- Read MPU6050 --------
  sensors_event_t a, g, t;
  mpu.getEvent(&a, &g, &t);
  
  float ax = a.acceleration.x;
  float ay = a.acceleration.y;
  float az = a.acceleration.z;
  
  // -------- FASTER Gravity estimation --------
  gx = gx * (1.0f - GRAV_ALPHA) + ax * GRAV_ALPHA;
  gy = gy * (1.0f - GRAV_ALPHA) + ay * GRAV_ALPHA;
  gz = gz * (1.0f - GRAV_ALPHA) + az * GRAV_ALPHA;
  
  // -------- Linear acceleration --------
  float lx = ax - gx;
  float ly = ay - gy;
  float lz = az - gz;
  
  // -------- VERTICAL motion magnitude --------
  float vertical = fabsf(lz);
  
  // -------- FASTER EMA --------
  static float vertEma = 0;
  vertEma = vertEma * (1.0f - MOVE_ALPHA) + vertical * MOVE_ALPHA;
  
  // 🔍 DEBUG OUTPUT - ALWAYS ON
  Serial.print("Raw Motion: "); 
  Serial.print(vertical, 3); 
  Serial.print(" | EMA: "); 
  Serial.print(vertEma, 3);
  Serial.print(" | Active: "); 
  Serial.println(motionActive ? "YES" : "NO");
  
  // -------- DETECT SUSTAINED MOTION --------
  
  // If motion exceeds threshold
  if (vertEma > MOTION_THRESHOLD) {
    lastMotionMs = now;  // Update last motion time
    
    // Start tracking if not already
    if (!motionActive) {
      motionActive = true;
      motionStartMs = now;
      Serial.println("📈 Motion started");
    }
  }
  
  // -------- CHECK IF MOTION HAS STOPPED --------
  if (motionActive) {
    uint32_t timeSinceMotion = now - lastMotionMs;
    
    Serial.print("  ⏱ Time since motion: ");
    Serial.print(timeSinceMotion);
    Serial.print(" ms | Threshold: ");
    Serial.println(MOTION_TIMEOUT_MS);
    
    // If no motion for MOTION_TIMEOUT_MS, end the shake
    if (timeSinceMotion > MOTION_TIMEOUT_MS) {
      uint32_t shakeDuration = lastMotionMs - motionStartMs;
      uint32_t timeSinceLastDetection = now - lastDetectionMs;
      
      Serial.print("🛑 Motion stopped. Duration: ");
      Serial.print(shakeDuration);
      Serial.println(" ms");
      
      // Check if it was sustained long enough AND cooldown passed
      if (shakeDuration >= MIN_SHAKE_DURATION_MS && 
          timeSinceLastDetection > COOLDOWN_MS) {
        onHandshakeDetected();
        lastDetectionMs = now;
      } else if (shakeDuration < MIN_SHAKE_DURATION_MS) {
        Serial.println("❌ Too short - not a handshake");
      } else {
        Serial.println("⏳ Cooldown active");
      }
      
      // Reset motion tracking
      motionActive = false;
    }
  }
}