#include "Display.h"
#include "Hardware.h"

Adafruit_SSD1306 display(SCREEN_WIDTH, SCREEN_HEIGHT, &Wire, -1);

void inicializarDisplay() {
  Wire.begin(21, 22); delay(250); 
  if(!display.begin(SSD1306_SWITCHCAPVCC, 0x3C)) { Serial.println(F("Fallo OLED")); }
  
  display.clearDisplay(); display.setTextColor(SSD1306_WHITE); display.setTextSize(1);
  display.setCursor(0, 0); display.print("v" + versionFirmware); display.display(); 

  for(int angulo = 0; angulo <= 360; angulo += 6) {
    float radianes = (angulo - 90) * 3.14159 / 180.0;
    display.fillCircle(64 + 18 * cos(radianes), 32 + 18 * sin(radianes), 4, SSD1306_WHITE);
    display.display(); delay(15); 
  }
  delay(300);
}

void despertarPantalla() { 
  if (!pantallaEncendida) { 
    display.ssd1306_command(SSD1306_DISPLAYON); 
    pantallaEncendida = true; 
  } 
}

void dibujarBateria() {
  bool estaCargando = (digitalRead(PIN_CARGA) == LOW);
  display.setTextSize(1); display.setCursor(85, 0); display.print(modoUSB ? "USB" : "BT");

  if (hayOcultas) {
    display.drawCircle(5, 3, 3, SSD1306_WHITE);
    display.drawLine(2, 6, 8, 0, SSD1306_WHITE);
  }

  if (!estaCargando && nivelBateria <= 15 && ((millis() / 500) % 2 == 0)) return; 
  display.drawRect(108, 0, 16, 7, SSD1306_WHITE); display.fillRect(124, 2, 2, 3, SSD1306_WHITE);
  int anchoCarga = estaCargando ? ((millis() / 400) % 4) * 4 : (nivelBateria >= 65 ? 12 : map(nivelBateria, 0, 64, 0, 11));
  if (anchoCarga > 0) display.fillRect(110, 2, anchoCarga, 3, SSD1306_WHITE); 
}

void dibujarTextoCentrado(String texto, int yBase) {
  if (texto.length() <= 10) { display.setTextSize(2); display.setCursor((128 - (texto.length() * 12)) / 2, yBase); display.print(texto); } 
  else if (texto.length() <= 21) { display.setTextSize(1); display.setCursor((128 - (texto.length() * 6)) / 2, yBase + 4); display.print(texto); } 
  else {
    display.setTextSize(1); String linea1 = texto.substring(0, 21); String linea2 = texto.substring(21);
    display.setCursor((128 - (linea1.length() * 6)) / 2, yBase); display.print(linea1);
    if (linea2.length() > 21) { linea2 = linea2.substring(0, 21); }
    display.setCursor((128 - (linea2.length() * 6)) / 2, yBase + 10); display.print(linea2);
  }
}

void actualizarOLED() {
  display.clearDisplay(); display.setTextColor(SSD1306_WHITE); display.invertDisplay(appSolo); dibujarBateria(); 

  if (appArtista.length() > 0) {
    String artistaCorto = appArtista.substring(0, min((int)appArtista.length(), 21));
    switch (appTema) {
      case 1: 
        dibujarTextoCentrado(">" + appNombre, 10); display.setTextSize(1); display.setCursor((128 - (artistaCorto.length() * 6)) / 2, 28); display.print(artistaCorto); 
        if (appMute) { display.setCursor(40, 48); display.setTextColor(SSD1306_BLACK, SSD1306_WHITE); display.print(" X MUTE X "); } 
        else { display.drawRect(0, 48, 128, 12, SSD1306_WHITE); for(int i = 0; i < map(appVolumen, 0, 100, 0, 20); i++) { display.fillRect(3 + (i * 6), 50, 4, 8, SSD1306_WHITE); } }
        break;
      case 2: 
        dibujarTextoCentrado(appNombre, 10); display.setTextSize(1); display.setCursor((128 - (artistaCorto.length() * 6)) / 2, 28); display.print(artistaCorto); 
        if (appMute) { display.setCursor(48, 48); display.setTextColor(SSD1306_BLACK, SSD1306_WHITE); display.print(" ERR:MUTE "); } 
        else { display.fillRect(0, 48, map(appVolumen, 0, 100, 0, 128), 12, SSD1306_WHITE); for(int i = 0; i < 128; i += 4) { display.drawLine(i, 48, i, 60, SSD1306_BLACK); } }
        break;
      case 3: 
        dibujarTextoCentrado(appNombre, 12); display.setTextSize(1); display.setCursor((128 - (artistaCorto.length() * 6)) / 2, 28); display.print(artistaCorto); 
        if (appMute) { display.setCursor(44, 48); display.setTextColor(SSD1306_BLACK, SSD1306_WHITE); display.print("   MUTE   "); } 
        else { display.drawRect(8, 48, 112, 10, SSD1306_WHITE); display.fillRect(10, 50, map(appVolumen, 0, 100, 0, 108), 6, SSD1306_WHITE); }
        break;
    }
  } else {
    switch (appTema) {
      case 1:
        dibujarTextoCentrado(">" + appNombre, 15); 
        if (appMute) { display.setTextSize(1); display.setCursor(40, 47); display.setTextColor(SSD1306_BLACK, SSD1306_WHITE); display.print(" X MUTE X "); } 
        else { display.drawRect(0, 45, 128, 12, SSD1306_WHITE); for(int i = 0; i < map(appVolumen, 0, 100, 0, 20); i++) { display.fillRect(3 + (i * 6), 47, 4, 8, SSD1306_WHITE); } }
        break;
      case 2:
        dibujarTextoCentrado(appNombre, 15); 
        if (appMute) { display.setTextSize(1); display.setCursor(48, 47); display.setTextColor(SSD1306_BLACK, SSD1306_WHITE); display.print(" ERR:MUTE "); } 
        else { display.fillRect(0, 42, map(appVolumen, 0, 100, 0, 128), 20, SSD1306_WHITE); for(int i = 0; i < 128; i += 6) { display.drawLine(i, 42, i, 62, SSD1306_BLACK); } }
        break;
      case 3:
        dibujarTextoCentrado(appNombre, 15); 
        if (appMute) { display.setTextSize(1); display.setCursor(48, 47); display.setTextColor(SSD1306_BLACK, SSD1306_WHITE); display.print(" MUTE "); } 
        else { display.drawRect(0, 45, 128, 12, SSD1306_WHITE); display.fillRect(2, 47, map(appVolumen, 0, 100, 0, 124), 8, SSD1306_WHITE); }
        break;
    }
  }
  display.display();
}

void renderizarEstadoConexion(int estadoConexion) {
  display.clearDisplay(); display.setTextColor(SSD1306_WHITE);
  dibujarTextoCentrado("MIXER", 15);
  dibujarTextoCentrado(estadoConexion == 1 ? "SINCRONIZANDO..." : "ESPERANDO LINKER32", 40);
  dibujarBateria(); display.display();
}