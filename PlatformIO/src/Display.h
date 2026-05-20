#ifndef DISPLAY_H
#define DISPLAY_H
#include <Wire.h>
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>

#define SCREEN_WIDTH 128
#define SCREEN_HEIGHT 64

extern Adafruit_SSD1306 display;

void inicializarDisplay();
void actualizarOLED();
void dibujarBateria();
void dibujarTextoCentrado(String texto, int yBase);
void despertarPantalla();
void renderizarEstadoConexion(int estadoConexion);

#endif