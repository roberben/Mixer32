#ifndef HARDWARE_H
#define HARDWARE_H
#include <Arduino.h>

// Asignación de Pines
#define PIN_ENC_A 32
#define PIN_ENC_B 33
#define PIN_ENC_SW 27
#define BTN_1 25
#define BTN_2 26
#define BTN_3 14
#define PIN_BAT 34
#define PIN_CARGA 13

// Declaración de Variables Globales
extern String versionFirmware;
extern bool modoUSB;
extern unsigned long ultimoComandoUSB;
extern bool balizaBLEActiva;

extern String appNombre;
extern String appArtista;
extern int appVolumen;
extern bool appMute;
extern bool appSolo;
extern bool hayOcultas;
extern bool ecoActivo;
extern int appTema;
extern bool conectadoPC;
extern int estadoConexionAnterior;

extern int nivelBateria;
extern unsigned long tiempoUltimaLecturaBat;
extern unsigned long tiempoUltimaAccion;
extern bool pantallaEncendida;
extern const unsigned long TIEMPO_DORMIR;

extern volatile bool giroDetectado;
extern volatile bool direccionArriba;

extern bool necesitaActualizarPantalla;

// Prototipos
void inicializarHardware();
void activarSwiftPair();
void leerHardware();
void enviarComando(String comando);

#endif