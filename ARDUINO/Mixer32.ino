#include <BluetoothSerial.h>
#include <Wire.h>
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>
#include <Update.h> // --- NUEVA LIBRERÍA OTA ---


#if !defined(CONFIG_BT_ENABLED) || !defined(CONFIG_BLUEDROID_ENABLED)
#error El Bluetooth no está habilitado. Comprueba la configuración de tu placa.
#endif

BluetoothSerial SerialBT;

// --- VERSIÓN DEL FIRMWARE ---
String versionFirmware = "0.9.3";

// --- PANTALLA OLED ---
#define SCREEN_WIDTH 128
#define SCREEN_HEIGHT 64
Adafruit_SSD1306 display(SCREEN_WIDTH, SCREEN_HEIGHT, &Wire, -1);

// --- PINES ESP32 DOIT 30-PIN ---
const int PIN_ENC_A = 32;
const int PIN_ENC_B = 33;
const int PIN_ENC_SW = 27;

const int BTN_1 = 25;
const int BTN_2 = 26;
const int BTN_3 = 14;

const int PIN_BAT = 34;

// --- VARIABLES DE ESTADO UI ---
String appNombre = "ESPERANDO...";
String appArtista = ""; 
int appVolumen = 0;
bool appMute = false;
bool appSolo = false;
bool ecoActivo = true; // Por defecto el modo ECO está encendido
int appTema = 3; 
bool conectadoPC = false;
int estadoConexionAnterior = -1;

// --- VARIABLES DE BATERÍA ---
int nivelBateria = 100;
unsigned long tiempoUltimaLecturaBat = 0;

// --- VARIABLES MODO REPOSO ---
unsigned long tiempoUltimaAccion = 0;
bool pantallaEncendida = true;
const unsigned long TIEMPO_DORMIR = 20000; // 20 segundos

// --- VARIABLES MÁQUINA DE ESTADOS ENCODER ---
volatile bool giroDetectado = false;
volatile bool direccionArriba = false;

// --- VARIABLES BOTONES (MULTIFUNCIÓN) ---
unsigned long tiempoPulsacionSW = 0;
bool estadoSWAnterior = HIGH;
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

void setup() {
  Serial.begin(115200);

  // INICIO DE BT ADELANTADO (Evita reinicios visibles por pico de corriente)
  SerialBT.begin("Mixer32_BT"); 
  SerialBT.setTimeout(2); 

  pinMode(PIN_ENC_A, INPUT_PULLUP);
  pinMode(PIN_ENC_B, INPUT_PULLUP);
  pinMode(PIN_ENC_SW, INPUT_PULLUP);
  pinMode(BTN_1, INPUT_PULLUP);
  pinMode(BTN_2, INPUT_PULLUP);
  pinMode(BTN_3, INPUT_PULLUP);
  
  pinMode(PIN_BAT, INPUT);

  attachInterrupt(digitalPinToInterrupt(PIN_ENC_A), isrEncoder, CHANGE);
  attachInterrupt(digitalPinToInterrupt(PIN_ENC_B), isrEncoder, CHANGE);

  Wire.begin(21, 22); 
  delay(250); 
  
  if(!display.begin(SSD1306_SWITCHCAPVCC, 0x3C)) { Serial.println(F("Fallo OLED")); }
  
  // --- ANIMACIÓN DE ARRANQUE EN ANILLO + VERSIÓN ---
  display.clearDisplay();
  display.setTextColor(SSD1306_WHITE);
  display.setTextSize(1);
  display.setCursor(0, 0);
  display.print("v" + versionFirmware);
  display.display(); 

  int centroX = 64; int centroY = 32; int radio = 18; int grosor = 4;   
  for(int angulo = 0; angulo <= 360; angulo += 6) {
    float radianes = (angulo - 90) * 3.14159 / 180.0;
    display.fillCircle(centroX + radio * cos(radianes), centroY + radio * sin(radianes), grosor, SSD1306_WHITE);
    display.display(); delay(15); 
  }
  delay(300); // Pequeña pausa para apreciar la animación

  // --- INICIO DE RADIO BT ---
  SerialBT.begin("Mixer32_BT"); 
  SerialBT.setTimeout(2); 
  tiempoUltimaAccion = millis(); 
}

void loop() {
  bool necesitaActualizarPantalla = false;
  unsigned long tiempoActual = millis();

  // --- MODO REPOSO INTELIGENTE: APAGAR PANTALLA ---
  if (ecoActivo && pantallaEncendida && (appArtista.length() == 0) && (tiempoActual - tiempoUltimaAccion > TIEMPO_DORMIR)) {
    display.ssd1306_command(SSD1306_DISPLAYOFF);
    pantallaEncendida = false;
  }

  // --- 0. LECTURA DE BATERÍA ---
  if (tiempoActual - tiempoUltimaLecturaBat > 5000) {
    tiempoUltimaLecturaBat = tiempoActual;
    int lecturaADC = analogRead(PIN_BAT);
    float voltaje = (lecturaADC / 4095.0) * 3.3 * 2.0; 
    int porcentaje = (voltaje - 3.2) / (4.2 - 3.2) * 100;
    if (porcentaje > 100) porcentaje = 100;
    if (porcentaje < 0) porcentaje = 0;

    if (porcentaje != nivelBateria) {
      nivelBateria = porcentaje;
      if (pantallaEncendida) { necesitaActualizarPantalla = true; estadoConexionAnterior = -1; }
    }
  }

  if (pantallaEncendida && nivelBateria <= 15) {
    static bool estadoBlinkAnterior = false;
    bool estadoBlinkActual = (tiempoActual / 500) % 2 == 0;
    if (estadoBlinkActual != estadoBlinkAnterior) {
      estadoBlinkAnterior = estadoBlinkActual;
      necesitaActualizarPantalla = true;
      estadoConexionAnterior = -1;
    }
  }

  // --- 1. GESTIÓN INTELIGENTE DE CONEXIÓN ---
  bool hayClienteBT = SerialBT.hasClient();
  if (!hayClienteBT && conectadoPC) { conectadoPC = false; }

  int estadoConexion = 0;
  if (conectadoPC) { estadoConexion = 2; } 
  else if (hayClienteBT) { estadoConexion = 1; } 
  else { estadoConexion = 0; }

  if (estadoConexion != estadoConexionAnterior) {
    estadoConexionAnterior = estadoConexion;
    tiempoUltimaAccion = tiempoActual; 
    despertarPantalla();
    
    if (estadoConexion == 0) {
      display.clearDisplay(); 
      display.setTextColor(SSD1306_WHITE);
      dibujarTextoCentrado("MIXER", 15);
      dibujarTextoCentrado("ESPERANDO LINKER32", 40);
      dibujarBateria(); 
      display.display();
    } 
    else if (estadoConexion == 1) {
      display.clearDisplay(); 
      display.setTextColor(SSD1306_WHITE);
      dibujarTextoCentrado("MIXER", 15);
      dibujarTextoCentrado("SINCRONIZANDO...", 40);
      dibujarBateria(); 
      display.display();
    }
    else if (estadoConexion == 2) { necesitaActualizarPantalla = true; }
  }

  // --- 2. LECTURA DE DATOS DEL PC + OTA ---
  while (SerialBT.available() > 0) {
    String data = SerialBT.readStringUntil('\n');
    data.trim();
    if (data.length() == 0) continue; 
    
    if (data == "PING") { 
        SerialBT.println("MIXER32_OK:" + versionFirmware); 
        conectadoPC = true; 
    }
    else if (data.startsWith("OTA_START:")) {
        int tamanoArchivo = data.substring(10).toInt();
        
        display.clearDisplay();
        display.drawRect(0, 0, 128, 64, SSD1306_WHITE);
        display.setTextSize(1);
        display.setCursor(22, 20);
        display.print("ACTUALIZANDO...");
        display.display();

        if (Update.begin(tamanoArchivo)) {
            // Vaciamos cualquier basura residual del Bluetooth
            while(SerialBT.available() > 0) { SerialBT.read(); }
            
            SerialBT.println("OTA_READY"); 
            
            int chunkSize = 512;
            uint8_t otaBuffer[512];
            size_t bytesEscritos = 0;
            bool otaError = false;
            int ultimoProgreso = -1; // --- VARIABLE PARA CONTROL DE REFRESCO VISUAL ---

            while (bytesEscritos < tamanoArchivo) {
                size_t aPedir = min((size_t)chunkSize, (size_t)(tamanoArchivo - bytesEscritos));
                
                unsigned long timeoutStart = millis();
                while (SerialBT.available() < aPedir) {
                    if (millis() - timeoutStart > 5000) { 
                        otaError = true;
                        break;
                    }
                    delay(1);
                }
                if (otaError) break;

                size_t leidos = SerialBT.readBytes(otaBuffer, aPedir);
                if (leidos == aPedir) {
                    size_t escritosAhora = Update.write(otaBuffer, leidos);
                    if (escritosAhora == leidos) {
                        bytesEscritos += leidos;
                        SerialBT.println("ACK");

                        // --- MOTOR VISUAL DE PROGRESO ---
                        int progreso = (bytesEscritos * 100) / tamanoArchivo;
                        // Solo redibujamos la OLED si el porcentaje ha cambiado (ahorra mucho tiempo)
                        if (progreso != ultimoProgreso) {
                            ultimoProgreso = progreso;
                            
                            display.clearDisplay();
                            display.drawRect(0, 0, 128, 64, SSD1306_WHITE);
                            
                            display.setTextSize(1);
                            display.setCursor(22, 16);
                            display.print("ACTUALIZANDO...");

                            // Dibujar marco de la barra de progreso
                            display.drawRect(14, 32, 100, 10, SSD1306_WHITE);
                            // Rellenar la barra según el progreso (de 0 a 96 pixeles de ancho)
                            display.fillRect(16, 34, map(progreso, 0, 100, 0, 96), 6, SSD1306_WHITE);

                            // Porcentaje en texto
                            display.setCursor(54, 48);
                            display.print(progreso);
                            display.print("%");
                            
                            display.display();
                        }
                        // --------------------------------

                    } else {
                        otaError = true;
                        break;
                    }
                } else {
                    otaError = true;
                    break;
                }
            }

            if (!otaError && Update.end() && Update.isFinished()) {
                display.clearDisplay();
                display.fillRect(0, 0, 128, 64, SSD1306_WHITE);
                display.setTextColor(SSD1306_BLACK);
                display.setCursor(30, 28);
                display.print("REINICIANDO");
                display.display();
                
                SerialBT.println("OTA_SUCCESS");
                delay(1500);
                ESP.restart(); 
            } else {
                display.clearDisplay();
                display.setCursor(5, 28);
                display.print("ERR OTA: ");
                display.print(Update.getError());
                display.display();
                
                SerialBT.println("OTA_FAIL");
                delay(5000);
                ESP.restart();
            }
        } else {
            SerialBT.println("OTA_FAIL");
        }
    }
    else if (data.startsWith("N:")) { appNombre = data.substring(2); necesitaActualizarPantalla = true; tiempoUltimaAccion = tiempoActual; despertarPantalla(); }
    else if (data.startsWith("A:")) { appArtista = data.substring(2); necesitaActualizarPantalla = true; tiempoUltimaAccion = tiempoActual; despertarPantalla(); }
    else if (data.startsWith("V:")) { appVolumen = data.substring(2).toInt(); necesitaActualizarPantalla = true; tiempoUltimaAccion = tiempoActual; despertarPantalla(); }
    else if (data.startsWith("M:")) { appMute = (data.substring(2) == "1"); necesitaActualizarPantalla = true; tiempoUltimaAccion = tiempoActual; despertarPantalla(); }
    else if (data.startsWith("S:")) { appSolo = (data.substring(2) == "1"); necesitaActualizarPantalla = true; tiempoUltimaAccion = tiempoActual; despertarPantalla(); }
    else if (data.startsWith("T:")) { appTema = data.substring(2).toInt(); necesitaActualizarPantalla = true; tiempoUltimaAccion = tiempoActual; despertarPantalla(); }
    else if (data.startsWith("E:")) { ecoActivo = (data.substring(2) == "1"); tiempoUltimaAccion = tiempoActual; despertarPantalla(); }
  }

  if (necesitaActualizarPantalla && conectadoPC && pantallaEncendida) { actualizarOLED(); }

  // --- 3. LECTURAS DE HARDWARE Y GESTIÓN DE TIEMPOS ---
  bool interaccionHardware = false;

  if (giroDetectado) {
    if (pantallaEncendida) { if (direccionArriba) { enviarComando("UP"); } else { enviarComando("DOWN"); } }
    giroDetectado = false; interaccionHardware = true;
  }

  bool estadoActualSW = digitalRead(PIN_ENC_SW);
  if (estadoActualSW == LOW && estadoSWAnterior == HIGH) { tiempoPulsacionSW = tiempoActual; } 
  else if (estadoActualSW == HIGH && estadoSWAnterior == LOW) {
    unsigned long duracion = tiempoActual - tiempoPulsacionSW;
    if (pantallaEncendida) { if (duracion > 600) { enviarComando("BSW_SHORT"); } else if (duracion > 50) { enviarComando("BSW_SOLO"); } }
    interaccionHardware = true;
  }
  estadoSWAnterior = estadoActualSW;

  bool estadoActualB1 = digitalRead(BTN_1);
  if (estadoActualB1 == LOW && estadoB1Anterior == HIGH) { tiempoPulsacionB1 = tiempoActual; } 
  else if (estadoActualB1 == HIGH && estadoB1Anterior == LOW) {
    unsigned long duracion = tiempoActual - tiempoPulsacionB1;
    if (pantallaEncendida) { if (duracion > 600) { enviarComando("B1_LONG"); } else if (duracion > 50) { enviarComando("B1"); } }
    interaccionHardware = true;
  }
  estadoB1Anterior = estadoActualB1;

  bool currentB2 = (digitalRead(BTN_2) == LOW);
  bool currentB3 = (digitalRead(BTN_3) == LOW);

  if (currentB2 && currentB3) {
    if (!comboProcesado) {
      if (pantallaEncendida) enviarComando("MEDIA_PLAY_PAUSE");
      comboProcesado = true;
      b2Procesado = true; 
      b3Procesado = true;
      interaccionHardware = true;
    }
  } else {
    if (!currentB2 && !currentB3) { comboProcesado = false; }

    if (currentB2 && !b2Presionado && !comboProcesado) {
      b2Presionado = true; tiempoPulsacionB2 = tiempoActual; b2Procesado = false;
    } 
    else if (currentB2 && b2Presionado && !b2Procesado) {
      if (tiempoActual - tiempoPulsacionB2 > 500) { 
        if (pantallaEncendida) enviarComando("MEDIA_NEXT");
        b2Procesado = true; interaccionHardware = true;
      }
    } 
    else if (!currentB2 && b2Presionado) {
      if (!b2Procesado && (tiempoActual - tiempoPulsacionB2 > 50)) { 
        if (pantallaEncendida) enviarComando("B2");
        interaccionHardware = true;
      }
      b2Presionado = false;
    }

    if (currentB3 && !b3Presionado && !comboProcesado) {
      b3Presionado = true; tiempoPulsacionB3 = tiempoActual; b3Procesado = false;
    } 
    else if (currentB3 && b3Presionado && !b3Procesado) {
      if (tiempoActual - tiempoPulsacionB3 > 500) { 
        if (pantallaEncendida) enviarComando("MEDIA_PREV");
        b3Procesado = true; interaccionHardware = true;
      }
    } 
    else if (!currentB3 && b3Presionado) {
      if (!b3Procesado && (tiempoActual - tiempoPulsacionB3 > 50)) {
        if (pantallaEncendida) enviarComando("B3");
        interaccionHardware = true;
      }
      b3Presionado = false;
    }
  }

  if (interaccionHardware) {
    tiempoUltimaAccion = tiempoActual;
    despertarPantalla();
  }
}

// --- FUNCIONES AUXILIARES ---
void despertarPantalla() {
  if (!pantallaEncendida) {
    display.ssd1306_command(SSD1306_DISPLAYON);
    pantallaEncendida = true;
  }
}

void enviarComando(String comando) { 
  if (conectadoPC) { SerialBT.println(comando); } 
}

void dibujarBateria() {
  if (nivelBateria >= 100) return; 
  if (nivelBateria <= 15) { if ((millis() / 500) % 2 == 0) return; } 
  
  display.drawRect(108, 0, 16, 7, SSD1306_WHITE);
  display.fillRect(124, 2, 2, 3, SSD1306_WHITE);
  int anchoCarga = map(nivelBateria, 0, 100, 0, 12);
  if (anchoCarga > 0) { display.fillRect(110, 2, anchoCarga, 3, SSD1306_WHITE); }
}

void dibujarTextoCentrado(String texto, int yBase) {
  if (texto.length() <= 10) {
    display.setTextSize(2);
    int anchoTexto = texto.length() * 12; 
    display.setCursor((128 - anchoTexto) / 2, yBase);
    display.print(texto);
  } 
  else if (texto.length() <= 21) {
    display.setTextSize(1);
    int anchoTexto = texto.length() * 6; 
    display.setCursor((128 - anchoTexto) / 2, yBase + 4); 
    display.print(texto);
  } 
  else {
    display.setTextSize(1);
    String linea1 = texto.substring(0, 21);
    String linea2 = texto.substring(21);
    
    int ancho1 = linea1.length() * 6;
    display.setCursor((128 - ancho1) / 2, yBase);
    display.print(linea1);
    
    if (linea2.length() > 21) { linea2 = linea2.substring(0, 21); }
    
    int ancho2 = linea2.length() * 6;
    display.setCursor((128 - ancho2) / 2, yBase + 10);
    display.print(linea2);
  }
}

void actualizarOLED() {
  display.clearDisplay();
  display.setTextColor(SSD1306_WHITE);
  display.invertDisplay(false); 
  
  if (appSolo) { display.invertDisplay(true); }
  dibujarBateria(); 

  // --- INTERFAZ EXCLUSIVA PARA MÚSICA REPRODUCIENDO ---
  if (appArtista.length() > 0) {
    String artistaCorto = appArtista;
    if (artistaCorto.length() > 21) artistaCorto = artistaCorto.substring(0, 21);
    
    switch (appTema) {
      case 1: // TEMA 1: ARCADE (Música)
        dibujarTextoCentrado(">" + appNombre, 0);
        display.setTextSize(1);
        display.setCursor((128 - (artistaCorto.length() * 6)) / 2, 25);
        display.print(artistaCorto);

        if (appMute) {
          display.setCursor(40, 48);
          display.setTextColor(SSD1306_BLACK, SSD1306_WHITE);
          display.print(" X MUTE X ");
        } else {
          display.drawRect(0, 48, 128, 12, SSD1306_WHITE);
          int bloques = map(appVolumen, 0, 100, 0, 20);
          for(int i = 0; i < bloques; i++) { display.fillRect(3 + (i * 6), 50, 4, 8, SSD1306_WHITE); }
        }
        break;

      case 2: // TEMA 2: CYBERPUNK (Música)
        dibujarTextoCentrado(appNombre, 0);
        display.setTextSize(1);
        display.setCursor((128 - (artistaCorto.length() * 6)) / 2, 25);
        display.print(artistaCorto);

        if (appMute) {
          display.setCursor(48, 48);
          display.setTextColor(SSD1306_BLACK, SSD1306_WHITE);
          display.print(" ERR:MUTE ");
        } else {
          int anchoVol = map(appVolumen, 0, 100, 0, 128);
          display.fillRect(0, 48, anchoVol, 12, SSD1306_WHITE);
          for(int i = 0; i < 128; i += 4) { display.drawLine(i, 48, i, 60, SSD1306_BLACK); }
        }
        break;

      case 3: // TEMA 3: PRO (Música)
        dibujarTextoCentrado(appNombre, 2);
        display.setTextSize(1);
        display.setCursor((128 - (artistaCorto.length() * 6)) / 2, 26);
        display.print(artistaCorto);

        if (appMute) {
          display.setCursor(44, 48);
          display.setTextColor(SSD1306_BLACK, SSD1306_WHITE);
          display.print("  MUTE  ");
        } else {
          display.drawRect(8, 48, 112, 10, SSD1306_WHITE);
          int anchoVol = map(appVolumen, 0, 100, 0, 108);
          display.fillRect(10, 50, anchoVol, 6, SSD1306_WHITE);
        }
        break;
    }
    display.display();
    return; 
  }

  // --- INTERFAZ ESTÁNDAR (Para aplicaciones normales) ---
  switch (appTema) {
    case 1: // TEMA 1: ARCADE 
      dibujarTextoCentrado(">" + appNombre, 10);
      if (appMute) {
        display.setTextSize(1); display.setCursor(40, 47); 
        display.setTextColor(SSD1306_BLACK, SSD1306_WHITE); display.print(" X MUTE X ");
      } else {
        display.drawRect(0, 45, 128, 12, SSD1306_WHITE); 
        int bloques = map(appVolumen, 0, 100, 0, 20); 
        for(int i = 0; i < bloques; i++) { display.fillRect(3 + (i * 6), 47, 4, 8, SSD1306_WHITE); }
      }
      break;

    case 2: // TEMA 2: CYBERPUNK 
      dibujarTextoCentrado(appNombre, 10);
      if (appMute) {
        display.setTextSize(1); display.setCursor(48, 47); 
        display.setTextColor(SSD1306_BLACK, SSD1306_WHITE); display.print(" ERR:MUTE ");
      } else {
        int anchoVolumen = map(appVolumen, 0, 100, 0, 128);
        display.fillRect(0, 42, anchoVolumen, 20, SSD1306_WHITE);
        for(int i = 0; i < 128; i += 6) { display.drawLine(i, 42, i, 62, SSD1306_BLACK); }
      }
      break;

    case 3: // TEMA 3: BROADCAST PRO 
      dibujarTextoCentrado(appNombre, 10);
      if (appMute) {
        display.setTextSize(1); display.setCursor(48, 47); 
        display.setTextColor(SSD1306_BLACK, SSD1306_WHITE); display.print(" MUTE ");
      } else {
        display.drawRect(0, 45, 128, 12, SSD1306_WHITE); 
        int anchoVolumen = map(appVolumen, 0, 100, 0, 124);
        display.fillRect(2, 47, anchoVolumen, 8, SSD1306_WHITE);
      }
      break;
  }
  display.display();
}