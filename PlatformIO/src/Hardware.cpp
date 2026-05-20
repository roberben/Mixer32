#include "Hardware.h"
#include "Display.h"
#include <BluetoothSerial.h>
#include <Update.h>
#include <BLEDevice.h>
#include <BLEUtils.h>
#include <BLEServer.h>
#include "esp_bt_device.h"
#include <string>

extern BluetoothSerial SerialBT;

// Variables internas
unsigned long tiempoPulsacionSW = 0;
static bool estadoSWAnterior = HIGH;
unsigned long tiempoPulsacionB1 = 0;
bool estadoB1Anterior = HIGH;
unsigned long tiempoPulsacionB2 = 0;
unsigned long tiempoPulsacionB3 = 0;
bool b2Presionado = false;
bool b3Presionado = false;
bool b2Procesado = false;
bool b3Procesado = false;
bool comboProcesado = false;

// --- MÁQUINA DE ESTADOS ANTI-REBOTES ENCODER ---
void IRAM_ATTR isrEncoder() {
  static uint8_t old_AB = 3;
  static int8_t encval = 0;
  static const int8_t enc_states[] = {0,-1,1,0,1,0,0,-1,-1,0,0,1,0,1,-1,0};
  old_AB <<= 2;
  old_AB |= ( (digitalRead(PIN_ENC_A) << 1) | digitalRead(PIN_ENC_B) ) & 0x03;
  encval += enc_states[(old_AB & 0x0F)];
  if (encval > 3) { direccionArriba = true; giroDetectado = true; encval = 0; } 
  else if (encval < -3) { direccionArriba = false; giroDetectado = true; encval = 0; }
}

void activarSwiftPair() {
  BLEDevice::init("Mixer32_BT"); 
  BLEAdvertising *pAdvertising = BLEDevice::getAdvertising();
  BLEAdvertisementData advData = BLEAdvertisementData();

  advData.setName("Mixer32_BT");
  advData.setFlags(0x02); 

  const uint8_t* mac = esp_bt_dev_get_address();
  char msd_bytes[14];
  msd_bytes[0] = 0x06; msd_bytes[1] = 0x00; msd_bytes[2] = 0x03; 
  msd_bytes[3] = 0x01; msd_bytes[4] = 0x80; 
  
  if (mac != NULL) {
    msd_bytes[5] = mac[0]; msd_bytes[6] = mac[1]; msd_bytes[7] = mac[2];
    msd_bytes[8] = mac[3]; msd_bytes[9] = mac[4]; msd_bytes[10]= mac[5];
  } else {
    for(int i=5; i<=10; i++) msd_bytes[i] = 0x00; 
  }

  // Class of Device (CoD) - Control Remoto Multimedia
  msd_bytes[11] = 0x0C; msd_bytes[12] = 0x04; msd_bytes[13] = 0x20; 

  std::string payload(msd_bytes, 14);
  advData.setManufacturerData(payload);
  pAdvertising->setAdvertisementData(advData);
  pAdvertising->start();
}

void inicializarHardware() {
  pinMode(PIN_ENC_A, INPUT_PULLUP); pinMode(PIN_ENC_B, INPUT_PULLUP); pinMode(PIN_ENC_SW, INPUT_PULLUP);
  pinMode(BTN_1, INPUT_PULLUP); pinMode(BTN_2, INPUT_PULLUP); pinMode(BTN_3, INPUT_PULLUP);
  pinMode(PIN_BAT, INPUT); pinMode(PIN_CARGA, INPUT_PULLUP);

  attachInterrupt(digitalPinToInterrupt(PIN_ENC_A), isrEncoder, CHANGE);
  attachInterrupt(digitalPinToInterrupt(PIN_ENC_B), isrEncoder, CHANGE);
}

void enviarComando(String comando) {
  if (conectadoPC) { 
    if (modoUSB) Serial.println(comando); 
    else SerialBT.println(comando); 
  } 
}

void leerHardware() {
  unsigned long tiempoActual = millis();

  // --- APAGADO AUTOMÁTICO DE LA BALIZA ---
  if (balizaBLEActiva && (tiempoActual > 60000 || conectadoPC)) {
    BLEDevice::getAdvertising()->stop();
    balizaBLEActiva = false;
  }

  // --- LECTURAS DE HARDWARE (LATENCIA CERO) ---
  bool interaccionHardware = false;

  if (giroDetectado) {
    if (pantallaEncendida) { enviarComando(direccionArriba ? "UP" : "DOWN"); }
    giroDetectado = false; interaccionHardware = true;
  }

  bool estadoActualSW = digitalRead(PIN_ENC_SW);
  if (estadoActualSW == LOW && estadoSWAnterior == HIGH) { tiempoPulsacionSW = tiempoActual; } 
  else if (estadoActualSW == HIGH && estadoSWAnterior == LOW) {
    if (pantallaEncendida) { enviarComando((tiempoActual - tiempoPulsacionSW > 600) ? "BSW_SHORT" : "BSW_SOLO"); }
    interaccionHardware = true;
  }
  estadoSWAnterior = estadoActualSW;

  bool estadoActualB1 = digitalRead(BTN_1);
  if (estadoActualB1 == LOW && estadoB1Anterior == HIGH) { tiempoPulsacionB1 = tiempoActual; } 
  else if (estadoActualB1 == HIGH && estadoB1Anterior == LOW) {
    if (pantallaEncendida) { enviarComando((tiempoActual - tiempoPulsacionB1 > 600) ? "B1_LONG" : "B1"); }
    interaccionHardware = true;
  }
  estadoB1Anterior = estadoActualB1;

  bool currentB2 = (digitalRead(BTN_2) == LOW);
  bool currentB3 = (digitalRead(BTN_3) == LOW);

  if (currentB2 && currentB3) {
    if (!comboProcesado) {
      if (pantallaEncendida) enviarComando("MEDIA_PLAY_PAUSE");
      comboProcesado = true; b2Procesado = true; b3Procesado = true; interaccionHardware = true;
    }
  } else {
    if (!currentB2 && !currentB3) { comboProcesado = false; }

    if (currentB2 && !b2Presionado && !comboProcesado) { b2Presionado = true; tiempoPulsacionB2 = tiempoActual; b2Procesado = false; } 
    else if (currentB2 && b2Presionado && !b2Procesado) { if (tiempoActual - tiempoPulsacionB2 > 500) { if (pantallaEncendida) enviarComando("MEDIA_NEXT"); b2Procesado = true; interaccionHardware = true; } } 
    else if (!currentB2 && b2Presionado) { if (!b2Procesado && (tiempoActual - tiempoPulsacionB2 > 50)) { if (pantallaEncendida) enviarComando("B2"); interaccionHardware = true; } b2Presionado = false; }

    if (currentB3 && !b3Presionado && !comboProcesado) { b3Presionado = true; tiempoPulsacionB3 = tiempoActual; b3Procesado = false; } 
    else if (currentB3 && b3Presionado && !b3Procesado) { if (tiempoActual - tiempoPulsacionB3 > 500) { if (pantallaEncendida) enviarComando("MEDIA_PREV"); b3Procesado = true; interaccionHardware = true; } } 
    else if (!currentB3 && b3Presionado) { if (!b3Procesado && (tiempoActual - tiempoPulsacionB3 > 50)) { if (pantallaEncendida) enviarComando("B3"); interaccionHardware = true; } b3Presionado = false; }
  }

  if (interaccionHardware) { tiempoUltimaAccion = tiempoActual; despertarPantalla(); }

  // --- REPOSO INTELIGENTE Y BATERÍA ---
  if (ecoActivo && pantallaEncendida && (nivelBateria > 15) && (tiempoActual - tiempoUltimaAccion > TIEMPO_DORMIR)) {
    display.ssd1306_command(SSD1306_DISPLAYOFF); pantallaEncendida = false;
  }

  if (tiempoActual - tiempoUltimaLecturaBat > 5000) {
    tiempoUltimaLecturaBat = tiempoActual;
    int porcentaje = constrain((((analogRead(PIN_BAT) / 4095.0) * 3.3 * 2.0) - 3.2) / (4.2 - 3.2) * 100, 0, 100);
    if (porcentaje != nivelBateria) {
      nivelBateria = porcentaje;
      if (pantallaEncendida) necesitaActualizarPantalla = true; 
      else if (nivelBateria <= 15) { despertarPantalla(); tiempoUltimaAccion = tiempoActual; necesitaActualizarPantalla = true; }
    }
  }

  bool estaCargando = (digitalRead(PIN_CARGA) == LOW);
  if (pantallaEncendida && estaCargando) {
    static unsigned long ultimoFrameCarga = 0;
    if (tiempoActual - ultimoFrameCarga > 400) { ultimoFrameCarga = tiempoActual; necesitaActualizarPantalla = true; }
  } else if (pantallaEncendida && nivelBateria <= 15) {
    static bool estadoBlinkAnterior = false;
    bool estadoBlinkActual = (tiempoActual / 500) % 2 == 0;
    if (estadoBlinkActual != estadoBlinkAnterior) { estadoBlinkAnterior = estadoBlinkActual; necesitaActualizarPantalla = true; }
  }

  // --- CONEXIONES ---
  bool hayClienteBT = SerialBT.hasClient();
  if (!hayClienteBT && conectadoPC && !modoUSB) { conectadoPC = false; }
  int estadoConexion = conectadoPC ? 2 : (hayClienteBT ? 1 : 0);
  
  if (estadoConexion != estadoConexionAnterior) {
    estadoConexionAnterior = estadoConexion; tiempoUltimaAccion = tiempoActual; despertarPantalla(); necesitaActualizarPantalla = true; 
  }

  // --- LECTURA EN LOTE (EVITA LAG OLED + OTA) ---
  while (Serial.available() > 0 || (SerialBT.available() > 0 && (tiempoActual - ultimoComandoUSB > 3000))) {
    Stream* puertoActivo = NULL; String data = "";
    if (Serial.available() > 0) { data = Serial.readStringUntil('\n'); modoUSB = true; ultimoComandoUSB = tiempoActual; puertoActivo = &Serial; } 
    else if (SerialBT.available() > 0) { data = SerialBT.readStringUntil('\n'); modoUSB = false; puertoActivo = &SerialBT; }

    if (puertoActivo != NULL) {
      data.trim();
      if (data.length() > 0) {
        if (data == "PING") { puertoActivo->println("MIXER32_OK:" + versionFirmware); conectadoPC = true; }
        else if (data.startsWith("OTA_START:")) {
          int tamanoArchivo = data.substring(10).toInt();
          bool otaError = false; 
          display.clearDisplay(); display.drawRect(0, 0, 128, 64, SSD1306_WHITE); display.setTextSize(1);
          display.setCursor(22, 20); display.print("ACTUALIZANDO..."); display.display();
          if (Update.begin(tamanoArchivo)) {
            while(puertoActivo->available() > 0) { puertoActivo->read(); } 
            puertoActivo->println("OTA_READY"); 
            size_t bytesEscritos = 0; int ultimoProgreso = -1; uint8_t otaBuffer[512];
            while (bytesEscritos < tamanoArchivo) {
              yield(); // Previene reinicios del Watchdog Timer (WDT) del ESP32
              size_t aPedir = min((size_t)512, (size_t)(tamanoArchivo - bytesEscritos));
              unsigned long timeoutStart = millis();
              size_t leidos = 0;
              while (leidos < aPedir) {
                if (millis() - timeoutStart > 5000) { otaError = true; break; }
                while (puertoActivo->available() > 0 && leidos < aPedir) {
                  otaBuffer[leidos++] = puertoActivo->read();
                }
                if (leidos < aPedir) delay(1);
              }
              if (otaError) break;
              if (leidos == aPedir && Update.write(otaBuffer, leidos) == leidos) {
                bytesEscritos += leidos; puertoActivo->println("ACK"); 
                int progreso = (bytesEscritos * 100) / tamanoArchivo;
                if (progreso != ultimoProgreso) {
                  ultimoProgreso = progreso; display.clearDisplay(); display.drawRect(0, 0, 128, 64, SSD1306_WHITE);
                  display.setTextSize(1); display.setCursor(22, 16); display.print("ACTUALIZANDO...");
                  display.drawRect(14, 32, 100, 10, SSD1306_WHITE); display.fillRect(16, 34, map(progreso, 0, 100, 0, 96), 6, SSD1306_WHITE);
                  display.setCursor(54, 48); display.print(progreso); display.print("%"); display.display();
                }
              } else { otaError = true; break; }
            }
            if (!otaError && Update.end() && Update.isFinished()) {
              display.clearDisplay(); display.fillRect(0, 0, 128, 64, SSD1306_WHITE); display.setTextColor(SSD1306_BLACK);
              display.setCursor(30, 28); display.print("REINICIANDO"); display.display();
              puertoActivo->println("OTA_SUCCESS"); delay(1500); ESP.restart(); 
            } else { otaError = true; }
          } else { otaError = true; }
          
          if (otaError) {
            display.clearDisplay(); display.setCursor(5, 28); display.print("ERR OTA"); display.display();
            puertoActivo->println("OTA_FAIL"); delay(5000); ESP.restart();
          }
        }
        else if (data.startsWith("N:")) { appNombre = data.substring(2); necesitaActualizarPantalla = true; tiempoUltimaAccion = tiempoActual; despertarPantalla(); }
        else if (data.startsWith("A:")) { appArtista = data.substring(2); necesitaActualizarPantalla = true; tiempoUltimaAccion = tiempoActual; despertarPantalla(); }
        else if (data.startsWith("V:")) { appVolumen = data.substring(2).toInt(); necesitaActualizarPantalla = true; tiempoUltimaAccion = tiempoActual; despertarPantalla(); }
        else if (data.startsWith("M:")) { appMute = (data.substring(2) == "1"); necesitaActualizarPantalla = true; tiempoUltimaAccion = tiempoActual; despertarPantalla(); }
        else if (data.startsWith("S:")) { appSolo = (data.substring(2) == "1"); necesitaActualizarPantalla = true; tiempoUltimaAccion = tiempoActual; despertarPantalla(); }
        else if (data.startsWith("H:")) { hayOcultas = (data.substring(2) == "1"); necesitaActualizarPantalla = true; tiempoUltimaAccion = tiempoActual; despertarPantalla(); }
        else if (data.startsWith("T:")) { appTema = data.substring(2).toInt(); necesitaActualizarPantalla = true; tiempoUltimaAccion = tiempoActual; despertarPantalla(); }
        else if (data.startsWith("E:")) { ecoActivo = (data.substring(2) == "1"); tiempoUltimaAccion = tiempoActual; despertarPantalla(); }
      }
    }
  }

  if (tiempoActual - ultimoComandoUSB <= 3000) { while(SerialBT.available()) SerialBT.read(); }
}