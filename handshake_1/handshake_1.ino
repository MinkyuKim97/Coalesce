//------------------------------------------------------------
// Board info: Waveshare ESP32 S3 Zero
// Upload Set up
// - Tool -> Board -> 'Waveshare ESP32 S3 Zero'
// - USB CDC On Boot: Enabled
// * Make sure to match the board info to control the builtInLED
//------------------------------------------------------------
// [secret.h]
// Make sure fill up the secret infos
// 1. WIFI SSID/PASSWORD list, can be multiple
// 2. Firestore database API key
// 3. Client ID, 4 digit number, matches with the DB data
//------------------------------------------------------------

#include "secrets.h"

// WIFI
#include <WiFi.h>
#include <WiFiClientSecure.h>
#include <HTTPClient.h>
// JSON read
#include <ArduinoJson.h>


// RX TX setting
#include <HardwareSerial.h>
const int BAUD = 115200;
const int TXInterval = 200;
const int RXTimeout = 600;
const char* msgLine = "MSG:";
int lastMsgTime = 0;
bool msgActivate = false;

const int RXPin = 5;
const int TXPin = 6;

HardwareSerial Uart(2);

String RXLine;
String line;
int lastTX = 0;
int counter = 0;


// Firestore Database
String idToken;
int tokenExpiryMs = 0;

// client info list
struct ClientInfo{
  String docPath;
  String Name;
};

ClientInfo currentClient;


// ------------------------------------------------------------------------------------------
// ------------------------------------------------------------------------------------------

// TLS
// Insecure way to detect the firebase
// but, this project isn't public or official, so...
// for efficiency
static inline void makeInsecureTLS(WiFiClientSecure &client){
  client.setInsecure();
}

// Firestore Database Auth
bool firebaseSignIn(){
  if(WiFi.status() != WL_CONNECTED){
    return false;
  }
  WiFiClientSecure client;
  makeInsecureTLS(client);
  
  HTTPClient https;
  String url = String("https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=") + FIREBASE_API_KEY;

  if(!https.begin(client, url)){
    Serial.println("[AUTH] https.begin failed");
    return false;
  }

  // Targeting JSON shape
  https.addHeader("Content-Type", "application/json");

  StaticJsonDocument<256> req;
  req["email"] = FIREBASE_EMAIL;
  req["password"] = FIREBASE_PASSWORD;
  req["returnSecureToken"] = true;

  String body;
  serializeJson(req,body);

  int code = https.POST(body);
  String resp = https.getString();
  https.end();

  Serial.printf("[AUTH] HTTP %d\n", code);
  if(code != 200){
    Serial.println(resp);
    return false;
  }

  StaticJsonDocument<4096> doc;
  auto err = deserializeJson(doc, resp);
  if(err){
    Serial.print("[AUTH] JSON parse error: ");
    Serial.println(err.c_str());
    return false;
  }

  idToken = doc["idToken"].as<String>();
  int expiresInSec = doc["expiresIn"].as<int>();
  tokenExpiryMs = millis() + (uint32_t)(max(60, expiresInSec - 60)) * 1000UL;
  
  Serial.println("[AUTH] idToken OK");
  return true;
}

bool ensureFirebaseAuth(){
  if(WiFi.status() != WL_CONNECTED){
    return false;
  }
  if(idToken.length() == 0 || millis() > tokenExpiryMs){
    return firebaseSignIn();
  }
  return true;
}

String fsBase(){
  return String("https://firestore.googleapis.com/v1/projects/")
       + FIREBASE_PROJECT_ID
       + "/databases/(default)/documents/";
}

// docPath: "clients/0000/..."
// Dig into the client infos
String fsDocUrl(const String& docPath){
  return fsBase() + docPath;
}

int firestoreGetRaw(const String docPath, String &outResp){
  if(!ensureFirebaseAuth()){
    return -1;
  }
  WiFiClientSecure client;
  makeInsecureTLS(client);

  HTTPClient https;
  if(!https.begin(client, fsDocUrl(docPath))){
    return -1;
  }
  https.addHeader("Authorization", "Bearer " + idToken);

  int code = https.GET();
  outResp = https.getString();
  https.end();

  return code;
}

//// Assigned to RXTask();
// When receive other client's id, compare it with current client's data
// {currnetClient} > clientConnection > {received client ID} > State(String value)
// If there's no received client ID in 'clientConnection' collection,
// make a data and set 'State' as 0
// If there's already received client ID in 'clientConnection' collection,
// apply 'State' ++;
bool firestoreClientConnectionUpdate(String otherID){
  if(!ensureFirebaseAuth()){
    Serial.println("FirebaseAuth Failed");
    return false;
  }

  // Preventing self pinging
  if(otherID == String(FIREBASE_CLIENTID)){
    Serial.println("Received same ClientID");
    return false;
  }

  String docPath = String("clients/") + FIREBASE_CLIENTID + "/clientConnection/" + otherID;

  // Access to the 'docPath' to confirm is it exist or not
  String resp;
  int code = firestoreGetRaw(docPath, resp);
  Serial.print("Code: ");
  Serial.println(code);

  // When it's not exist, create one
  if(code == 404){
    WiFiClientSecure client;
    makeInsecureTLS(client);

    HTTPClient https;
    String url = fsDocUrl(docPath) + "?updateMask.fieldPaths=State";

    if(!https.begin(client, url)){
      Serial.println("HTTPS Begin Falied");
      return false;
    }

    https.addHeader("Authorization", "Bearer " + idToken);
    https.addHeader("Content-Type", "application/json");

    StaticJsonDocument<256> body;
    JsonObject fields = body.createNestedObject("fields");
    fields["State"]["stringValue"] = "0";

    String payload;
    serializeJson(body, payload);

    int c2 = https.PATCH(payload);
    String r2 = https.getString();
    https.end();

    if(c2 != 200){
      Serial.println(r2);
      return false;
    }
    return true;
  }

  // When it's exist, parse 'State' and increase
  if(code == 200){
    int current = 0;
    {
      DynamicJsonDocument doc(4096);
      auto err = deserializeJson(doc, resp);
      if(err){
        Serial.println("Failed deserializeJson 200");
        return false;
      }
      const char* s = doc["fields"]["State"]["stringValue"] | "0";
      current = atoi(s);
      if(current < 0){
        current = 0;
      }
    }
    int next = current + 1;
    
    WiFiClientSecure client;
    makeInsecureTLS(client);

    HTTPClient https;
    String url = fsDocUrl(docPath) + "?updateMask.fieldPaths=State";

    if(!https.begin(client,url)){
      Serial.println("Failed https.begin 200");
      return false;
    }

    https.addHeader("Authorization", "Bearer " + idToken);
    https.addHeader("Content-Type", "application/json");

    StaticJsonDocument<256> body;
    JsonObject fields = body.createNestedObject("fields");

    char buf[16];
    snprintf(buf, sizeof(buf), "%d", next);
    fields["State"]["stringValue"] = buf;

    String payload;
    serializeJson(body, payload);

    int c2 = https.PATCH(payload);
    String r2 = https.getString();
    https.end();

    if(c2 != 200){
        Serial.println(r2);
        return false;
    }
    return true;  
  }
  // other errors
  Serial.printf("[FS] GET %s -> HTTP %d\n", docPath.c_str(), code);
  Serial.println(resp);
  return false;
}


// WIFI Connection
void connectWiFi() {
  if (WiFi.status() == WL_CONNECTED) return;

  static bool started = false;
  static unsigned long lastAttempt = 0;

  if (millis() - lastAttempt < 5000) return;
  lastAttempt = millis();

  WiFi.mode(WIFI_STA);
  WiFi.setSleep(false);

  if (!started) {
    Serial.println("Scanning WiFi...");
    int n = WiFi.scanNetworks();

    if (n <= 0) {
      Serial.println("No WiFi networks found");
      return;
    }

    for (int i = 0; i < n; i++) {
      String found = WiFi.SSID(i);

      for (int j = 0; j < WIFI_COUNT; j++) {
        if (found == WIFI_SSIDS[j]) {
          Serial.print("Connecting to ");
          Serial.println(WIFI_SSIDS[j]);

          WiFi.begin(WIFI_SSIDS[j], WIFI_PASSWORDS[j]);
          started = true;
          Serial.print("WIFI connected with: ");
          Serial.println(WIFI_SSIDS[j]);
          return;
        }
      }
    }
    Serial.println("No known WiFi detected");
  }
}

// RX TX 
// Action after receive the msg
void onMsgLine(String line){
  Serial.print("Receive, ");
  Serial.println(line);

  // Action

  // Firestore: create(State=0) if missing, else State++
  bool tryUpdate = firestoreClientConnectionUpdate(line);

  if(tryUpdate){
    rgbLedWrite(RGB_BUILTIN, 0, 0,RGB_BRIGHTNESS);
  }else{
    rgbLedWrite(RGB_BUILTIN, RGB_BRIGHTNESS, 0, 0);
  }
  delay(500);
}

// Action after lost the msg
void onMsgLost() {
  Serial.println("Connection lost");
  rgbLedWrite(RGB_BUILTIN, 0, 0, 0);
  digitalWrite(RGB_BUILTIN, LOW);
  delay(1000);

}

void TXTask(){
  int now  = millis();
  if(now - lastTX < TXInterval){
    return;
  }
  lastTX = now;
  Uart.print('\r');
  Uart.print(msgLine);
  Uart.print(FIREBASE_CLIENTID);
  Uart.print('\n');
}

void RXTask(){
  while (Uart.available()>0){
    char c = (char)Uart.read();
    if(c == '\r'){
      continue;
    }

    if (c == '\n') {
      RXLine.trim();
      
      if(RXLine.startsWith(msgLine)){
        String cmd = RXLine.substring(4);
        cmd.trim();
        Serial.println(cmd);

        lastMsgTime = millis();
        if(!msgActivate){
          msgActivate = true;
        }        
        onMsgLine(cmd);
      }
      RXLine = "";
      continue;
    }else{
      RXLine += c;

    }
  }
}

void msgLostTask(){
  if(!msgActivate){
    return;
  }
  int now = millis();
  if(now - lastMsgTime > RXTimeout){
    msgActivate = false;
    onMsgLost();
  }
}


void setup() {
  Serial.begin(115200);
  digitalWrite(RGB_BUILTIN, LOW);  

  delay(200);

  // Uart.begin(BAUD, SERIAL_8N1, RXPin, TXPin);


  Serial.println("ESP32 ready");
  Serial.print("Current Client ID is: ");
  Serial.println(FIREBASE_CLIENTID);

  connectWiFi();
  delay(200);
  digitalWrite(RGB_BUILTIN, HIGH);  



}

void loop() {
  // WIFI reconnect attempt
  if (WiFi.status() != WL_CONNECTED) {
    static uint32_t lastTry = 0;
    if (millis() - lastTry > 5000) {
      lastTry = millis();
      connectWiFi();
    }
  }

  // Looping RX TX task
  TXTask();
  RXTask();
  msgLostTask();

}


