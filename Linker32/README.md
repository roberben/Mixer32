# 🎛️ Linker32 & MIXER32

Un ecosistema completo de hardware y software diseñado para ofrecerte un control físico, táctil y absoluto sobre el mezclador de audio de Windows. 

Compuesto por **Linker32** (una aplicación de escritorio en C# .NET) y **MIXER32** (un periférico basado en ESP32 con pantalla OLED), este proyecto te permite gestionar el volumen independiente de tus aplicaciones, mutear, aislar canales y controlar tu multimedia sin tener que hacer Alt+Tab.

![Linker32](https://img.shields.io/badge/OS-Windows_10%20%7C%2011-blue)
![C#](https://img.shields.io/badge/Software-C%23_.NET-purple)
![ESP32](https://img.shields.io/badge/Hardware-ESP32-green)
![Bluetooth](https://img.shields.io/badge/Conexi%C3%B3n-Bluetooth-0082FC)

## ✨ Características Principales

### 🖥️ Software (Linker32 - PC)
![Captura de pantalla de Linker32](IMAGES/Linker32_0.9.3.png)
* **Control por Aplicación:** Extrae dinámicamente las sesiones de audio de Windows (Spotify, Chrome, Discord, Juegos) y te permite controlarlas individualmente.
* **Auto-Foco Inteligente:** Detecta qué ventana tienes activa y cambia el control del encoder automáticamente a esa aplicación. Monitoriza canales nuevos en tiempo real.
* **Bypass de Sandbox:** Extracción de iconos de procesos a bajo nivel para una interfaz gráfica rica y moderna.
* **Modo Invisible:** Funciona en la bandeja del sistema de Windows con opción de auto-arranque silencioso.
* **Sistema OTA (Over-The-Air) Integrado:** Actualiza el firmware del hardware por Bluetooth o cable directamente desde el PC con un protocolo blindado "Ping-Pong" antipérdidas.

### 🕹️ Hardware (MIXER32)
![Foto del dispositivo MIXER32](IMAGES/MIXER32_img1.png)
* **Feedback Visual OLED:** Muestra el nombre de la app, el artista/canción actual, el nivel de volumen, barras de progreso de actualización y estados de conexión.
* **Temas Visuales:** 3 estilos gráficos intercambiables en caliente (ARC - Arcade, CYB - Cyberpunk, PRO - Broadcast).
* **Modo ECO:** Gestión inteligente de energía que apaga la pantalla tras un periodo de inactividad.
* **Botones Multimedia y Tácticos:** Soporte para atajos de Play/Pause, Next, Prev, Mute Maestro y Modo SOLO (aislar el audio de una sola aplicación).
  
![Foto del dispositivo MIXER32](IMAGES/MIXER32_img2.png)

## 🛠️ Requisitos de Hardware
![Foto de materiales DIY](IMAGES/MaterialesHW.png)

Para construir tu propio MIXER32 necesitarás:
* Placa de desarrollo **DOIT ESP32 DevKit V1** (con Bluetooth clásico activado).
* Batería **LiPo de 3.7V (2000 mAh)**.
* Módulo de carga de litio **TP4056** (con protección de sobredescarga).
* Interruptor deslizante **SS12F15VG4** (SPDT).
* Pantalla **OLED SSD1306** (I2C).
* Encoder rotativo **KY-040** (con pulsador integrado).
* 3 **Interruptores de teclado Cherry MX** mecánicos.
* 2 **Resistencias de 100kΩ** (para el divisor de tensión del lector de batería).
* 4 tornillos **M3x10 Allen**.
* 8 tornillos **M2x8 Phillips**.

---

## 🖨️ Impresión 3D y Comunidad
![Modelado 3D y personalización de MIXER32](IMAGES/3dprint.png)

El diseño de la carcasa está pensado para ser totalmente ergonómico, simulando la estética de un equipo de *broadcast* profesional con la pantalla OLED perfectamente angulada para su lectura en escritorio.

* **Descarga el Modelo Original:** Tienes los archivos listos para imprimir en el perfil oficial de [MakerWorld](https://makerworld.com/es/models/2818918-mixer32#profileId-3138938).
* **¡Aporta tu propia versión!** 🚀 Este proyecto nace con espíritu abierto y modular. Te animamos e invitamos a que diseñes tus propias modificaciones de la carcasa: añade nuevos soportes, cambia la disposición de la botonera, crea variaciones estéticas o adáptalo a otros componentes. ¡Haz un *Fork* del proyecto o comparte tus *Remixes* en MakerWorld para que la comunidad siga creciendo!

---

### 🔌 Esquema de Conexiones (Wiring)
![Hazlo realidad!!!](IMAGES/esquemaSemiRealV2.png)

Realiza las siguientes conexiones físicas utilizando la serigrafía grabada en tu placa DOIT V1:

[Esquema ortodoxo](https://raw.githubusercontent.com/roberben/Linker32/refs/heads/master/IMAGES/esquemaElectricoV2.png)

**1. Sistema de Alimentación Principal (Batería, TP4056 e Interruptor)**
*El objetivo es que el módulo TP4056 gestione la recarga de forma segura y el interruptor corte la corriente hacia el ESP32, permitiendo cargar la batería incluso con el dispositivo apagado.*
* **Batería LiPo:** Cable rojo (Positivo) al pad **`B+`** del TP4056; cable negro (Negativo) al pad **`B-`**.
* **Tierra General:** Pad **`OUT-`** del TP4056 conectado directamente a un pin **`GND`** del ESP32.
* **Interruptor Deslizante (SS12F15VG4):** Pad **`OUT+`** del TP4056 al pin **central** del interruptor. Uno de los pines **laterales** del interruptor va directo al pin **`VIN`** (o `5V`) del ESP32.

**2. Pantalla OLED SSD1306 (Bus I2C)**
* **VCC:** Pin `3V3` del ESP32
* **GND:** Pin `GND`
* **SDA:** Pin `D21`
* **SCL:** Pin `D22`

**3. Encoder Rotativo (KY-040)**
* **VCC:** Pin `3V3` del ESP32
* **GND:** Pin `GND`
* **CLK (Pin A):** Pin `D32`
* **DT (Pin B):** Pin `D33`
* **SW (Pulsador):** Pin `D27` *(Configurado con PULLUP interno, se activa en LOW)*

**4. Botonera Multimedia (Interruptores Cherry MX)**
*Un pin de cada interruptor se suelda al GPIO correspondiente y el otro contacto va común a la línea `GND` general.*
* **Botón 1 (Mute / Desmutear Todo):** Pin `D25`
* **Botón 2 (Canal Siguiente / Multimedia Next):** Pin `D26`
* **Botón 3 (Canal Anterior / Multimedia Prev):** Pin `D14`

**5. Lector de Nivel de Batería (Monitor de porcentaje)**
* Para medir la carga sin descargar la batería de forma pasiva cuando el interruptor está apagado, el divisor de tensión se conecta **después** del interruptor deslizante (en la línea que va hacia el pin `VIN` del ESP32).
* Coloca las dos resistencias de 100kΩ en serie conectando un extremo a la línea `VIN` (salida del interruptor) y el otro extremo a la línea `GND`. El punto de unión central entre ambas resistencias se conecta directamente al pin **`D34`**.

**6. Sensor de Estado de Carga (Animación de batería)**
* Para que la pantalla OLED muestre la animación de carga activa, el ESP32 debe detectar cuándo el cargador está trabajando mediante un "cable espía" que monitoriza el estado del chip TP4056.
* Cable Espía: Un extremo se suelda directamente a la patilla número 7 del chip integrado TP4056 (el integrado negro de 8 patas). El otro extremo va conectado al pin D13 del ESP32.
* Funcionamiento: Esta patilla baja a 0.0V (LOW) únicamente cuando el proceso de carga está activo (LED rojo encendido). El firmware detecta este estado y activa automáticamente el motor de animación de la pila en el OLED.

---

## 🚀 Instalación y Uso

### 1. El Hardware (Flasheo del ESP32)

Tienes dos opciones para instalar el firmware en tu placa ESP32:

**Opción A: Instalación Fácil (Recomendada) 🌐**
Puedes instalar el firmware directamente desde el navegador de tu ordenador, sin necesidad de descargar código ni instalar entornos de desarrollo.
1. Conecta tu ESP32 por USB al ordenador.
2. Entra al **[Instalador Web de MIXER32](https://roberben.github.io/Linker32/)** *(requiere Google Chrome o Microsoft Edge).*
3. Haz clic en **Connect**, selecciona el puerto COM de tu placa y dale a instalar. 

**Opción B: Compilación Manual (Para Desarrolladores) 🛠️**
Si deseas modificar el código fuente:
1. Instala [Visual Studio Code](https://code.visualstudio.com/) y la extensión de **PlatformIO**. *(Arduino IDE ya no está soportado).*
2. Clona este repositorio y ábrelo con PlatformIO. Éste se encargará de descargar las dependencias.
3. Conecta tu placa ESP32 por cable USB.
4. Usa el botón de **Upload** de PlatformIO para compilar y flashear.

### 2. El Software (Windows)
1. Descarga la última versión desde la pestaña de **Releases** de GitHub.
2. Ejecuta `Linker32.exe`.
3. Vincula el dispositivo Bluetooth llamado `Mixer32_BT` en la configuración de Windows (o usa el cable USB).
4. La aplicación de escritorio detectará el puerto COM de forma automática, sincronizará los canales y podrás empezar a operar.

## 📡 Protocolo OTA Seguro
Linker32 incluye un sistema de flasheo integrado (inalámbrico por Bluetooth o por cable USB) de nivel industrial. Si el software detecta que el hardware tiene un firmware desactualizado, te ofrecerá actualizarlo de forma transparente. Utiliza una arquitectura estricta de transferencia por bloques de 512 bytes sincronizados por `ACK` junto con un chequeo de integridad local de firma de arranque (`Magic Byte 0xE9`), lo que hace que el proceso sea completamente inmune a fallos de conexión o pérdidas de datos.

## 👨‍💻 Autor
Creado y desarrollado por **Rober Ben**.

Si este proyecto te ha resultado útil, ¡no dudes en dejar una ⭐ en el repositorio!
