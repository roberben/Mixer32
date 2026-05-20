#include <Arduino.h>
#include <BluetoothSerial.h>
#include "Hardware.h"
#include "Display.h"

#if !defined(CONFIG_BT_ENABLED) || !defined(CONFIG_BLUEDROID_ENABLED)
#error El Bluetooth no está habilitado. Comprueba la configuración de tu placa.
#endif

BluetoothSerial SerialBT;

// --- VARIABLES GLOBALES ---
String versionFirmware = "0.9.6";
bool modoUSB = false;
unsigned long ultimoComandoUSB = 0;
bool balizaBLEActiva = true;

String appNombre = "ESPERANDO...";
String appArtista = ""; 
int appVolumen = 0;
bool appMute = false;
bool appSolo = false;
bool hayOcultas = false;
bool ecoActivo = true; 
int appTema = 3;
bool conectadoPC = false;
int estadoConexionAnterior = -1;

int nivelBateria = 100;
unsigned long tiempoUltimaLecturaBat = 0;
unsigned long tiempoUltimaAccion = 0;
bool pantallaEncendida = true;
const unsigned long TIEMPO_DORMIR = 20000; 

volatile bool giroDetectado = false;
volatile bool direccionArriba = false;

bool necesitaActualizarPantalla = false;

void setup() {
  Serial.setRxBufferSize(1024);
  Serial.begin(115200); Serial.setTimeout(2); 
  SerialBT.begin("Mixer32_BT"); SerialBT.setTimeout(2); 
  
  activarSwiftPair();
  inicializarHardware();
  inicializarDisplay();
  
  tiempoUltimaAccion = millis(); 
}

void loop() {
  necesitaActualizarPantalla = false;
  
  leerHardware();

  // --- DIBUJADO FINAL SEGURIZADO ---
  // Centralizamos la decisión de dibujar tras procesar todo el hardware y comandos
  if (necesitaActualizarPantalla && pantallaEncendida) { 
    if (conectadoPC) { 
      actualizarOLED(); 
    } else {
      bool hayClienteBT = SerialBT.hasClient();
      int estadoConexion = conectadoPC ? 2 : (hayClienteBT ? 1 : 0);
      renderizarEstadoConexion(estadoConexion);
    }
  }
}