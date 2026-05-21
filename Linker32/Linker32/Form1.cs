using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.CoreAudioApi;
using Microsoft.Win32;
using Windows.Media.Control;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Reflection;

namespace Linker32
{
    public partial class Form1 : Form
    {
        // --- CONSTANTES DE VERSIÓN ---
        private const string VERSION_ACTUAL_PC = "0.9.6";
        private const string VERSION_HARDWARE_INCLUIDA = "0.9.6";

        // --- APIS GRÁFICAS Y DE VENTANAS ---
        [DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        [DllImport("user32.DLL", EntryPoint = "ReleaseCapture")]
        private extern static void ReleaseCapture();

        [DllImport("user32.DLL", EntryPoint = "SendMessage")]
        private extern static void SendMessage(IntPtr hWnd, int wMsg, int wParam, int lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        // --- APIS DE BAJO NIVEL PARA EXTRACCIÓN DE ICONOS (BYPASS SANDBOX) ---
        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll")]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, System.Text.StringBuilder lpExeName, ref uint lpdwSize);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        // --- ORDENES DIRECTAS AL MOTOR MULTIMEDIA (FRANCOTIRADOR) ---
        private async void EjecutarAccionMultimediaNativa(string accion, string appEspecifica = "")
        {
            try
            {
                if (gestorMediosWindows == null) return;

                GlobalSystemMediaTransportControlsSession sesionObjetivo = null;
                string nombreBuscar = appEspecifica.ToLower();

                if (string.IsNullOrEmpty(nombreBuscar) && indiceCanalActual > 0 && indiceCanalActual <= canalesActuales.Count)
                {
                    nombreBuscar = canalesActuales[indiceCanalActual - 1].Nombre.ToLower();
                }

                if (!string.IsNullOrEmpty(nombreBuscar))
                {
                    var sesionesMedia = gestorMediosWindows.GetSessions();

                    foreach (var s in sesionesMedia)
                    {
                        string idApp = (s.SourceAppUserModelId ?? "").ToLower();
                        bool esLaSeleccionada = false;

                        if (nombreBuscar == "spotify" && idApp.Contains("spotify")) esLaSeleccionada = true;
                        else if (nombreBuscar == "amazon music" && (idApp.Contains("amazon") || idApp.Contains("prime"))) esLaSeleccionada = true;
                        else if (nombreBuscar == "chrome" && idApp.Contains("chrome")) esLaSeleccionada = true;
                        else if (nombreBuscar == "edge" && (idApp.Contains("edge") || idApp.Contains("msedge") || idApp.Contains("webview"))) esLaSeleccionada = true;
                        else if (nombreBuscar == "discord" && idApp.Contains("discord")) esLaSeleccionada = true;
                        else if (idApp.Contains(nombreBuscar) || nombreBuscar.Contains(idApp)) esLaSeleccionada = true;

                        if (esLaSeleccionada)
                        {
                            sesionObjetivo = s;
                            break;
                        }
                    }
                }

                if (sesionObjetivo == null)
                {
                    sesionObjetivo = gestorMediosWindows.GetCurrentSession();
                }

                if (sesionObjetivo == null) return;

                if (accion == "NEXT") await sesionObjetivo.TrySkipNextAsync();
                else if (accion == "PREV") await sesionObjetivo.TrySkipPreviousAsync();
                else if (accion == "PLAY_PAUSE") await sesionObjetivo.TryTogglePlayPauseAsync();
            }
            catch { }
        }

        // --- ESTRUCTURAS DE DATOS ---
        public class GrupoAudio
        {
            public string Nombre = "";
            public uint PidPrincipal = 0;
            public List<AudioSessionControl> Sesiones = new List<AudioSessionControl>();

            public float Volumen
            {
                get { return Sesiones.Count > 0 ? Sesiones[0].SimpleAudioVolume.Volume : 0; }
                set { foreach (var s in Sesiones) s.SimpleAudioVolume.Volume = value; }
            }
            public bool Mute
            {
                get { return Sesiones.Count > 0 ? Sesiones[0].SimpleAudioVolume.Mute : false; }
                set { foreach (var s in Sesiones) s.SimpleAudioVolume.Mute = value; }
            }
        }

        public class InfoMedia
        {
            public string Titulo = "";
            public string Artista = "";
            public bool Reproduciendo = false;
        }

        private SerialPort? puertoExterior;
        private MMDevice? dispositivoAudio;
        private int indiceCanalActual = 0;
        private List<GrupoAudio> canalesActuales = new List<GrupoAudio>();

        // --- CACHÉS Y MEMORIA DE HIERRO ---
        private Dictionary<uint, string> cacheNombres = new Dictionary<uint, string>();
        private Dictionary<uint, Image?> cacheIconos = new Dictionary<uint, Image?>();
        private Dictionary<string, InfoMedia> infoMediosGlobal = new Dictionary<string, InfoMedia>(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, float> memoriaVolumenApp = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, bool> memoriaMuteApp = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, bool> memoriaHideApp = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private bool buscandoHardware = false;
        private CancellationTokenSource? ctsBusqueda;

        // --- VARIABLE CLAVE PARA OTA ---
        private bool otaEnProgreso = false;

        private bool masterMuteActivado = false;
        private HashSet<string> appsMuteadasAntesDelMaster = new HashSet<string>();
        private HashSet<uint> procesosConocidos = new HashSet<uint>();

        private bool modoSoloActivado = false;
        private int indiceCanalSolo = -1;

        private DateTime ultimoGiroVolumen = DateTime.MinValue;
        private bool exteriorMostrandoVolumenTemporal = false;
        private string ultimoMensajeExterior = "";

        private DateTime ultimoGiroEncoder = DateTime.MinValue;
        private DateTime ultimoClickBoton = DateTime.MinValue;

        private bool autoFocoActivado = false;
        private uint ultimoPIDFoco = 0;
        private int ultimoConteoCanales = 0; // Para el parche del Auto-Foco

        private DateTime ultimaRespuestaPing = DateTime.Now;
        private DateTime ultimoEnvioPing = DateTime.MinValue;

        private int temaActualVisual = 3;
        private GlobalSystemMediaTransportControlsSessionManager? gestorMediosWindows;

        private NotifyIcon? iconoBandeja;
        private ContextMenuStrip? menuBandeja;
        private FlowLayoutPanel? panelContenedor;
        private System.Windows.Forms.Timer? timerRefrescoUI;
        private Label? lblEstadoConexion;
        private CheckBox? chkInicioAuto;
        private CheckBox? chkAutoFoco;
        private CheckBox? chkModoEco;
        private bool modoEcoActivado = true;
        private Button? btnTema1;
        private Button? btnTema2;
        private Button? btnTema3;

        private bool iniciarOculto = false;

        public Form1()
        {
            string[] args = Environment.GetCommandLineArgs();
            foreach (string arg in args) if (arg.ToLower() == "-hidden") iniciarOculto = true;

            temaActualVisual = LeerEstadoTema();

            InitializeComponent();
            ConfigurarInterfazModerna();
            ConfigurarBandejaSistema();

            try
            {
                var enumerator = new MMDeviceEnumerator();
                dispositivoAudio = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }
            catch { }

            _ = LoopLecturaMedios();
            ReiniciarBusqueda();

            _ = VerificarActualizaciones();
        }

        private async Task LoopLecturaMedios()
        {
            try { gestorMediosWindows = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync(); }
            catch { return; }

            while (true)
            {
                try
                {
                    if (gestorMediosWindows != null)
                    {
                        var sesiones = gestorMediosWindows.GetSessions();
                        var nuevoDic = new Dictionary<string, InfoMedia>(StringComparer.OrdinalIgnoreCase);

                        foreach (var s in sesiones)
                        {
                            string idApp = s.SourceAppUserModelId ?? "";
                            if (string.IsNullOrEmpty(idApp)) continue;

                            string appName = "";
                            string nLower = idApp.ToLower();
                            if (nLower.Contains("spotify")) appName = "Spotify";
                            else if (nLower.Contains("amazon") || nLower.Contains("prime")) appName = "Amazon Music";
                            else if (nLower.Contains("chrome")) appName = "Chrome";
                            else if (nLower.Contains("edge") || nLower.Contains("msedge") || nLower.Contains("webview")) appName = "Edge";
                            else if (nLower.Contains("discord")) appName = "Discord";
                            else appName = idApp;

                            var playbackInfo = s.GetPlaybackInfo();
                            bool isPlaying = playbackInfo != null && playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

                            var propsTask = s.TryGetMediaPropertiesAsync().AsTask();

                            if (await Task.WhenAny(propsTask, Task.Delay(500)) == propsTask)
                            {
                                var props = propsTask.Result;
                                string titulo = props?.Title ?? "";
                                string artista = props?.Artist ?? "";

                                if (!nuevoDic.ContainsKey(appName) || isPlaying)
                                {
                                    nuevoDic[appName] = new InfoMedia { Titulo = titulo, Artista = artista, Reproduciendo = isPlaying };
                                }
                            }
                        }
                        infoMediosGlobal = nuevoDic;
                    }
                }
                catch { }

                await Task.Delay(1000);
            }
        }

        private async Task VerificarActualizaciones()
        {
            string repoOwner = "roberben";
            string repoName = "Linker32";
            string urlApi = $"https://api.github.com/repos/{repoOwner}/{repoName}/releases/latest";

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "Linker32-Updater");

                    string response = await client.GetStringAsync(urlApi);
                    JsonNode? json = JsonNode.Parse(response);

                    if (json == null) return;

                    string tagLanzamiento = json["tag_name"]?.ToString() ?? "";
                    string versionLanzamientoStr = tagLanzamiento.Replace("v", "").Trim();

                    if (Version.TryParse(VERSION_ACTUAL_PC, out Version? currentVersion) &&
                        Version.TryParse(versionLanzamientoStr, out Version? latestVersion))
                    {
                        // Normalizamos las versiones para evitar que 0.9.5.0 sea mayor que 0.9.5
                        Version normCurrent = new Version(currentVersion.Major, currentVersion.Minor, Math.Max(currentVersion.Build, 0), Math.Max(currentVersion.Revision, 0));
                        Version normLatest = new Version(latestVersion.Major, latestVersion.Minor, Math.Max(latestVersion.Build, 0), Math.Max(latestVersion.Revision, 0));

                        if (normLatest > normCurrent)
                        {
                            DialogResult dialogResult = MessageBox.Show(
                                $"¡Hay una nueva versión disponible de Linker32 ({tagLanzamiento})!\n\n¿Quieres descargarla e instalarla ahora?",
                                "Actualización Disponible",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Information);

                            if (dialogResult == DialogResult.Yes)
                            {
                                JsonArray? assets = json["assets"]?.AsArray();
                                string downloadUrl = "";

                                if (assets != null)
                                {
                                    foreach (var asset in assets)
                                    {
                                        string name = asset["name"]?.ToString() ?? "";
                                        if (name.EndsWith(".exe"))
                                        {
                                            downloadUrl = asset["browser_download_url"]?.ToString() ?? "";
                                            break;
                                        }
                                    }
                                }

                                if (!string.IsNullOrEmpty(downloadUrl))
                                {
                                    await DescargarYEjecutarInstalador(downloadUrl);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private async Task DescargarYEjecutarInstalador(string url)
        {
            try
            {
                this.BeginInvoke(new Action(() => {
                    lblEstadoConexion!.Text = "DESCARGANDO ACTUALIZACIÓN...";
                    lblEstadoConexion.ForeColor = Color.DeepSkyBlue;
                }));

                string rutaTemp = Path.Combine(Path.GetTempPath(), "Actualizacion_Mixer32.exe");

                using (HttpClient client = new HttpClient())
                using (var s = await client.GetStreamAsync(url))
                using (var fs = new FileStream(rutaTemp, FileMode.Create))
                {
                    await s.CopyToAsync(fs);
                }

                Process.Start(new ProcessStartInfo(rutaTemp) { UseShellExecute = true });
                Application.Exit();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Fallo al descargar la actualización: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // --- SISTEMA OTA HARDWARE: PROTOCOLO PING-PONG COMPLETO ---
        private async Task ActualizarFirmwareOTA()
        {
            if (puertoExterior == null || !puertoExterior.IsOpen)
            {
                otaEnProgreso = false;
                return;
            }

            try
            {
                this.BeginInvoke(new Action(() => {
                    lblEstadoConexion!.Text = "VERIFICANDO BINARIO LOCAL...";
                    lblEstadoConexion.ForeColor = Color.Yellow;
                }));

                var assembly = Assembly.GetExecutingAssembly();
                using (Stream? stream = assembly.GetManifestResourceStream("Linker32.firmware.bin"))
                {
                    if (stream == null)
                    {
                        MessageBox.Show("Error: No se encontró el firmware incrustado en el programa.", "Error Interno", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        otaEnProgreso = false;
                        ManejarDesconexion();
                        return;
                    }

                    byte[] firmware = new byte[stream.Length];
                    await stream.ReadAsync(firmware, 0, firmware.Length);

                    // --- SALVAVIDAS CRÍTICO: Comprobación del Magic Byte ---
                    if (firmware.Length == 0 || firmware[0] != 0xE9)
                    {
                        MessageBox.Show("¡ALERTA! El archivo firmware.bin incrustado no es válido o está corrupto.\n\nUn firmware de ESP32 siempre debe empezar por el byte 0xE9. Asegúrate de haber incrustado el archivo 'Mixer32.ino.bin' correcto.", "Fallo de Integridad Local", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        otaEnProgreso = false;
                        ManejarDesconexion();
                        return;
                    }

                    await Task.Run(async () =>
                    {
                        // 1. Limpiamos las tuberías de comunicación
                        await Task.Delay(500);
                        if (puertoExterior.BytesToRead > 0) puertoExterior.ReadExisting();

                        // 2. Avisamos a la placa del tamaño
                        puertoExterior.WriteLine($"OTA_START:{firmware.Length}");

                        // 3. HANDSHAKE: Esperamos el OK (máx 15 seg)
                        puertoExterior.ReadTimeout = 15000;
                        string respuesta = "";
                        while (respuesta != "OTA_READY" && respuesta != "OTA_FAIL")
                        {
                            respuesta = puertoExterior.ReadLine().Trim();
                        }

                        if (respuesta == "OTA_FAIL") throw new Exception("La placa no pudo inicializar el espacio de memoria.");

                        this.BeginInvoke(new Action(() => { lblEstadoConexion!.Text = "ENVIANDO FIRMWARE: 0%"; }));

                        // 4. BUCLE DE TRANSMISIÓN PASO A PASO (MODO EQUILIBRADO: 512 bytes)
                        int chunkSize = 512;
                        int ultimoPorcentaje = -1;

                        for (int i = 0; i < firmware.Length; i += chunkSize)
                        {
                            int enviar = Math.Min(chunkSize, firmware.Length - i);
                            puertoExterior.Write(firmware, i, enviar);

                            int porcentaje = (int)(((long)(i + enviar) * 100) / firmware.Length);

                            if (porcentaje != ultimoPorcentaje)
                            {
                                ultimoPorcentaje = porcentaje;
                                this.BeginInvoke(new Action(() => {
                                    lblEstadoConexion!.Text = $"ENVIANDO FIRMWARE: {porcentaje}%";
                                }));
                            }

                            // Esperamos OBLIGATORIAMENTE el ACK
                            puertoExterior.ReadTimeout = 8000;
                            string ack = puertoExterior.ReadLine().Trim();
                            if (ack != "ACK")
                            {
                                throw new Exception("Desincronización del protocolo. Se recibió: " + ack);
                            }
                        }

                        // 5. Esperamos confirmación final
                        this.BeginInvoke(new Action(() => { lblEstadoConexion!.Text = "VERIFICANDO INTEGRIDAD DEL BINARIO..."; }));

                        puertoExterior.ReadTimeout = 15000;
                        string resultado = puertoExterior.ReadLine().Trim();
                        if (resultado != "OTA_SUCCESS")
                        {
                            throw new Exception("La placa rechazó el binario final por fallo de integridad.");
                        }
                    });

                    MessageBox.Show("Actualización de hardware completada con éxito.\nEl dispositivo se está reiniciando.", "OTA Completado", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    otaEnProgreso = false;
                    ManejarDesconexion();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Fallo crítico durante el proceso OTA: " + ex.Message, "Error OTA", MessageBoxButtons.OK, MessageBoxIcon.Error);
                otaEnProgreso = false;
                ManejarDesconexion();
            }
        }

        protected override void SetVisibleCore(bool value)
        {
            if (iniciarOculto) { value = false; if (!this.IsHandleCreated) CreateHandle(); iniciarOculto = false; }
            base.SetVisibleCore(value);
        }

        private void ReiniciarBusqueda()
        {
            if (buscandoHardware) return;
            ctsBusqueda?.Cancel();
            ctsBusqueda = new CancellationTokenSource();
            Task.Run(() => BuscarHardware(ctsBusqueda.Token));
        }

        private void ConfigurarInterfazModerna()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.Size = new Size(440, 700);
            this.BackColor = Color.FromArgb(24, 24, 28);
            this.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 25, 25));
            this.StartPosition = FormStartPosition.CenterScreen;

            Panel pnlHeader = new Panel { Dock = DockStyle.Top, Height = 65, BackColor = Color.FromArgb(32, 32, 36) };
            pnlHeader.MouseDown += (s, e) => { ReleaseCapture(); SendMessage(this.Handle, 0x112, 0xf012, 0); };

            Label lblTitulo = new Label { Text = "LINKER32 • MIXER32", ForeColor = Color.White, Font = new Font("Segoe UI", 10, FontStyle.Bold), Location = new Point(15, 12), AutoSize = true };
            lblTitulo.MouseDown += (s, e) => { ReleaseCapture(); SendMessage(this.Handle, 0x112, 0xf012, 0); };

            lblEstadoConexion = new Label { Text = "BUSCANDO HARDWARE...", ForeColor = Color.DarkOrange, Font = new Font("Segoe UI", 7, FontStyle.Bold), AutoSize = true, Location = new Point(15, 35) };

            Button btnCerrar = new Button { Text = "✕", Size = new Size(40, 40), Location = new Point(this.Width - 40, 0), FlatStyle = FlatStyle.Flat, ForeColor = Color.Gray, Font = new Font("Arial", 11, FontStyle.Bold), Cursor = Cursors.Hand };
            btnCerrar.FlatAppearance.BorderSize = 0;
            btnCerrar.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 17, 35);
            btnCerrar.Click += (s, e) => this.Hide();

            Label lblFirma = new Label { Text = "BY ROBER BEN", ForeColor = Color.FromArgb(60, 60, 65), Font = new Font("Segoe UI", 8, FontStyle.Bold), AutoSize = true, Location = new Point(290, 16) };

            pnlHeader.Controls.Add(lblTitulo); pnlHeader.Controls.Add(lblFirma); pnlHeader.Controls.Add(lblEstadoConexion); pnlHeader.Controls.Add(btnCerrar);

            Panel pnlFooter = new Panel { Dock = DockStyle.Bottom, Height = 45, BackColor = Color.FromArgb(20, 20, 22) };

            chkInicioAuto = new CheckBox { Text = "INICIAR AUTO", Font = new Font("Segoe UI", 7, FontStyle.Bold), Location = new Point(15, 12), AutoSize = true, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            chkInicioAuto.Checked = ComprobarInicioAutomatico();
            chkInicioAuto.ForeColor = chkInicioAuto.Checked ? Color.MediumSpringGreen : Color.DimGray;
            chkInicioAuto.CheckedChanged += (s, e) => { ConfigurarInicioAutomatico(chkInicioAuto.Checked); chkInicioAuto.ForeColor = chkInicioAuto.Checked ? Color.MediumSpringGreen : Color.DimGray; };

            chkAutoFoco = new CheckBox { Text = "AUTO-FOCO", Font = new Font("Segoe UI", 7, FontStyle.Bold), Location = new Point(105, 12), AutoSize = true, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            autoFocoActivado = LeerEstadoAutoFoco();
            chkAutoFoco.Checked = autoFocoActivado;
            chkAutoFoco.ForeColor = chkAutoFoco.Checked ? Color.DeepSkyBlue : Color.DimGray;
            chkAutoFoco.CheckedChanged += (s, e) => { autoFocoActivado = chkAutoFoco.Checked; GuardarEstadoAutoFoco(autoFocoActivado); chkAutoFoco.ForeColor = autoFocoActivado ? Color.DeepSkyBlue : Color.DimGray; };

            chkModoEco = new CheckBox { Text = "MODO ECO", Font = new Font("Segoe UI", 7, FontStyle.Bold), Location = new Point(190, 12), AutoSize = true, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            modoEcoActivado = LeerEstadoEco();
            chkModoEco.Checked = modoEcoActivado;
            chkModoEco.ForeColor = chkModoEco.Checked ? Color.DarkOrange : Color.DimGray;
            chkModoEco.CheckedChanged += (s, e) => {
                modoEcoActivado = chkModoEco.Checked;
                GuardarEstadoEco(modoEcoActivado);
                chkModoEco.ForeColor = modoEcoActivado ? Color.DarkOrange : Color.DimGray;
                ActualizarExterior(true);
            };

            btnTema1 = CrearBotonTema("ARC", 280, 1);
            btnTema2 = CrearBotonTema("CYB", 320, 2);
            btnTema3 = CrearBotonTema("PRO", 360, 3);
            ActualizarEstiloBotonesTema();

            pnlFooter.Controls.Add(chkInicioAuto);
            pnlFooter.Controls.Add(chkAutoFoco);
            pnlFooter.Controls.Add(chkModoEco);
            pnlFooter.Controls.Add(btnTema1);
            pnlFooter.Controls.Add(btnTema2);
            pnlFooter.Controls.Add(btnTema3);

            panelContenedor = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(15, 80, 15, 20), FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent };

            this.Controls.Add(panelContenedor); this.Controls.Add(pnlHeader); this.Controls.Add(pnlFooter);
            pnlHeader.BringToFront(); pnlFooter.BringToFront();

            timerRefrescoUI = new System.Windows.Forms.Timer { Interval = 100 };
            timerRefrescoUI.Tick += (s, e) => {
                try
                {
                    // --- NUEVO: SISTEMA ANTIZOMBIE DE RECONEXIÓN DE AUDIO ---
                    if (dispositivoAudio == null)
                    {
                        try
                        {
                            var enumerator = new MMDeviceEnumerator();
                            dispositivoAudio = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                        }
                        catch
                        {
                            return; // Si el audio de Windows aún no está listo, abortamos este tick y probamos luego
                        }
                    }
                    // ---------------------------------------------------------

                    ActualizarDashboardPC();
                    ProcesarAutoFoco();

                    if (otaEnProgreso) return;

                    if (puertoExterior == null || !puertoExterior.IsOpen) { ReiniciarBusqueda(); }
                    else
                    {
                        ActualizarExterior();
                        if ((DateTime.Now - ultimoEnvioPing).TotalSeconds > 2) { try { puertoExterior.WriteLine("PING"); } catch { ManejarDesconexion(); return; } ultimoEnvioPing = DateTime.Now; }
                        if ((DateTime.Now - ultimaRespuestaPing).TotalSeconds > 5) { ManejarDesconexion(); }
                    }
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    // El dispositivo de audio ha sido desconectado o el servicio falló.
                    // Forzamos su reinicialización en el próximo tick.
                    dispositivoAudio = null;
                }
                catch (Exception)
                {
                    // Para cualquier otro fallo general en el loop de UI, limpiamos para intentar recuperar
                    dispositivoAudio = null;
                }
            };
            timerRefrescoUI.Start();
        }

        private Button CrearBotonTema(string texto, int x, int temaNum)
        {
            Button btn = new Button { Text = texto, Size = new Size(35, 22), Location = new Point(x, 10), FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 6, FontStyle.Bold), Cursor = Cursors.Hand };
            btn.FlatAppearance.BorderSize = 0;
            btn.Click += (s, e) => { temaActualVisual = temaNum; GuardarEstadoTheme(temaActualVisual); ActualizarEstiloBotonesTema(); ActualizarExterior(true); };
            return btn;
        }

        private void ActualizarEstiloBotonesTema()
        {
            if (btnTema1 != null) { btnTema1.BackColor = temaActualVisual == 1 ? Color.White : Color.FromArgb(40, 40, 45); btnTema1.ForeColor = temaActualVisual == 1 ? Color.Black : Color.Gray; }
            if (btnTema2 != null) { btnTema2.BackColor = temaActualVisual == 2 ? Color.White : Color.FromArgb(40, 40, 45); btnTema2.ForeColor = temaActualVisual == 2 ? Color.Black : Color.Gray; }
            if (btnTema3 != null) { btnTema3.BackColor = temaActualVisual == 3 ? Color.White : Color.FromArgb(40, 40, 45); btnTema3.ForeColor = temaActualVisual == 3 ? Color.Black : Color.Gray; }
        }

        private void ProcesarAutoFoco()
        {
            if (!autoFocoActivado || canalesActuales.Count == 0) return;

            IntPtr hwndFoco = GetForegroundWindow();
            if (hwndFoco != IntPtr.Zero)
            {
                uint pidFoco; GetWindowThreadProcessId(hwndFoco, out pidFoco);

                bool forzarRevision = (canalesActuales.Count != ultimoConteoCanales);
                ultimoConteoCanales = canalesActuales.Count;

                if (pidFoco != ultimoPIDFoco || forzarRevision)
                {
                    ultimoPIDFoco = pidFoco;
                    string nombreProcesoFoco = ObtenerNombreProcesoSeguro(pidFoco);

                    for (int i = 0; i < canalesActuales.Count; i++)
                    {
                        if (canalesActuales[i].Nombre.Equals(nombreProcesoFoco, StringComparison.OrdinalIgnoreCase))
                        {
                            if (memoriaHideApp.TryGetValue(canalesActuales[i].Nombre, out bool isHidden) && isHidden)
                            {
                                break;
                            }
                            if (indiceCanalActual != i + 1)
                            {
                                indiceCanalActual = i + 1;
                                ultimoGiroVolumen = DateTime.Now;
                                ActualizarExterior();
                            }
                            break;
                        }
                    }
                }
            }
        }

        private async Task BuscarHardware(CancellationToken token)
        {
            if (buscandoHardware) return;
            buscandoHardware = true;

            string carpetaAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mixer32");
            if (!Directory.Exists(carpetaAppData)) Directory.CreateDirectory(carpetaAppData);
            string arc = Path.Combine(carpetaAppData, "puerto_mixer32.txt");

            while (!token.IsCancellationRequested)
            {
                string ult = File.Exists(arc) ? File.ReadAllText(arc).Trim() : "";
                string[] puertos = SerialPort.GetPortNames();
                var listaPuertos = new List<string>(puertos);
                if (listaPuertos.Contains(ult)) { listaPuertos.Remove(ult); listaPuertos.Insert(0, ult); }

                foreach (string p in listaPuertos)
                {
                    if (token.IsCancellationRequested) break;
                    SerialPort? tempPort = null;
                    try
                    {
                        tempPort = new SerialPort(p, 115200) { DtrEnable = true, RtsEnable = true, ReadTimeout = 1500, WriteTimeout = 1000 };
                        tempPort.Open(); await Task.Delay(1000, token); tempPort.WriteLine("PING");

                        string respuestaHardware = tempPort.ReadLine().Trim();

                        if (respuestaHardware.StartsWith("MIXER32_OK"))
                        {
                            puertoExterior = tempPort;

                            puertoExterior.DataReceived += (s, e) => {
                                try
                                {
                                    if (otaEnProgreso) return;
                                    if (puertoExterior != null && puertoExterior.IsOpen)
                                    {
                                        string cmd = puertoExterior.ReadLine().Trim();
                                        this.BeginInvoke(new Action(() => ProcesarComando(cmd)));
                                    }
                                }
                                catch { ManejarDesconexion(); }
                            };

                            File.WriteAllText(arc, p);

                            ultimaRespuestaPing = DateTime.Now; ultimoEnvioPing = DateTime.Now;
                            this.BeginInvoke(new Action(() => { lblEstadoConexion!.Text = $"CONECTADO AL EXTERIOR: {p}"; lblEstadoConexion.ForeColor = Color.MediumSpringGreen; ActualizarExterior(true); }));
                            buscandoHardware = false;

                            string[] partes = respuestaHardware.Split(':');
                            string versionActualHardware = partes.Length > 1 ? partes[1] : "0.0.0";

                            if (Version.TryParse(VERSION_HARDWARE_INCLUIDA, out Version? vPc) &&
                                Version.TryParse(versionActualHardware, out Version? vHw))
                            {
                                if (vPc > vHw)
                                {
                                    otaEnProgreso = true;
                                    this.BeginInvoke(new Action(async () => {
                                        DialogResult res = MessageBox.Show($"Tu dispositivo Mixer32 tiene el firmware v{versionActualHardware}, pero el programa requiere la v{VERSION_HARDWARE_INCLUIDA} para funcionar correctamente.\n\n¿Quieres actualizar el hardware ahora por Bluetooth?", "Actualización de Hardware", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                                        if (res == DialogResult.Yes)
                                        {
                                            await ActualizarFirmwareOTA();
                                        }
                                        else
                                        {
                                            otaEnProgreso = false;
                                            ultimaRespuestaPing = DateTime.Now;
                                        }
                                    }));
                                }
                            }
                            return;
                        }
                    }
                    catch { if (tempPort != null) { if (tempPort.IsOpen) tempPort.Close(); tempPort.Dispose(); } }
                }
                this.BeginInvoke(new Action(() => { lblEstadoConexion!.Text = "BUSCANDO HARDWARE..."; lblEstadoConexion.ForeColor = Color.DarkOrange; }));
                await Task.Delay(3000, token);
            }
            buscandoHardware = false;
        }

        private string ObtenerNombreProcesoSeguro(uint pid)
        {
            if (pid == 0) return "SISTEMA";
            if (cacheNombres.TryGetValue(pid, out string nombre)) return nombre;
            try
            {
                using (Process p = Process.GetProcessById((int)pid))
                {
                    nombre = p.ProcessName;

                    string nomLower = nombre.ToLower();
                    if (nomLower.Contains("amazon") || nomLower.Contains("prime")) nombre = "Amazon Music";
                    else if (nomLower.Contains("chrome")) nombre = "Chrome";
                    else if (nomLower.Contains("edge") || nomLower.Contains("webview")) nombre = "Edge";
                    else if (nomLower.Contains("spotify")) nombre = "Spotify";
                    else if (nomLower.Contains("discord")) nombre = "Discord";

                    cacheNombres[pid] = nombre;
                    return nombre;
                }
            }
            catch
            {
                cacheNombres[pid] = "AUDIO FANTASMA";
                return "AUDIO FANTASMA";
            }
        }

        private string ObtenerRutaProceso(int pid)
        {
            uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
            IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProcess == IntPtr.Zero) return "";

            try
            {
                System.Text.StringBuilder buffer = new System.Text.StringBuilder(1024);
                uint size = (uint)buffer.Capacity;
                if (QueryFullProcessImageName(hProcess, 0, buffer, ref size))
                {
                    return buffer.ToString();
                }
            }
            finally
            {
                CloseHandle(hProcess);
            }
            return "";
        }

        private Image? ObtenerIconoSeguro(uint pid)
        {
            if (cacheIconos.TryGetValue(pid, out Image? ico)) return ico;
            try
            {
                string ruta = "";

                try
                {
                    using (Process p = Process.GetProcessById((int)pid))
                    {
                        ruta = p.MainModule?.FileName ?? "";
                    }
                }
                catch
                {
                    ruta = ObtenerRutaProceso((int)pid);
                }

                if (!string.IsNullOrEmpty(ruta))
                {
                    ico = Icon.ExtractAssociatedIcon(ruta)?.ToBitmap();
                    cacheIconos[pid] = ico;
                    return ico;
                }
            }
            catch { }

            cacheIconos[pid] = null;
            return null;
        }

        private void ActualizarDashboardPC()
        {
            if (dispositivoAudio == null || panelContenedor == null) return;

            var manager = dispositivoAudio.AudioSessionManager;
            manager.RefreshSessions();

            var diccionarioGrupos = new Dictionary<string, GrupoAudio>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < manager.Sessions.Count; i++)
            {
                var s = manager.Sessions[i];
                if (s.IsSystemSoundsSession || s.GetProcessID == 0 || (int)s.State == 2) continue;

                uint pid = s.GetProcessID;
                string nombre = ObtenerNombreProcesoSeguro(pid);

                if (nombre == "AUDIO FANTASMA") continue;

                if (!diccionarioGrupos.ContainsKey(nombre))
                {
                    diccionarioGrupos[nombre] = new GrupoAudio { Nombre = nombre, PidPrincipal = pid };
                }
                diccionarioGrupos[nombre].Sesiones.Add(s);
            }

            var listaGrupos = new List<GrupoAudio>(diccionarioGrupos.Values);
            listaGrupos.Sort((a, b) => string.Compare(a.Nombre, b.Nombre, StringComparison.OrdinalIgnoreCase));

            canalesActuales = listaGrupos;

            if (indiceCanalActual > canalesActuales.Count) indiceCanalActual = 0;

            foreach (var c in canalesActuales)
            {
                if (memoriaVolumenApp.TryGetValue(c.Nombre, out float volGuardado))
                {
                    if (Math.Abs(c.Volumen - volGuardado) > 0.01f)
                    {
                        c.Volumen = volGuardado;
                    }
                }
                else
                {
                    memoriaVolumenApp[c.Nombre] = c.Volumen;
                }

                if (memoriaMuteApp.TryGetValue(c.Nombre, out bool muteGuardado))
                {
                    if (c.Mute != muteGuardado && !masterMuteActivado)
                    {
                        c.Mute = muteGuardado;
                    }
                }
                else
                {
                    memoriaMuteApp[c.Nombre] = c.Mute;
                }

                if (masterMuteActivado) c.Mute = true;
            }

            int total = canalesActuales.Count + 1;
            if (panelContenedor.Controls.Count != total)
            {
                panelContenedor.Controls.Clear();
                panelContenedor.Controls.Add(CrearPanelUI("MASTER", null, 0));
                for (int i = 0; i < canalesActuales.Count; i++)
                {
                    panelContenedor.Controls.Add(CrearPanelUI(canalesActuales[i].Nombre.ToUpper(), ObtenerIconoSeguro(canalesActuales[i].PidPrincipal), i + 1));
                }
            }

            for (int i = 0; i < panelContenedor.Controls.Count; i++)
            {
                Panel pnl = (Panel)panelContenedor.Controls[i];
                Panel pnlBarra = (Panel)pnl.Controls["pnlFondo"].Controls["pnlVol"];
                Button btnMute = (Button)pnl.Controls["btnMute"];
                Button? btnSolo = pnl.Controls.ContainsKey("btnSolo") ? (Button)pnl.Controls["btnSolo"] : null;
                Button? btnHide = pnl.Controls.ContainsKey("btnHide") ? (Button)pnl.Controls["btnHide"] : null;

                Button? btnPrev = pnl.Controls.ContainsKey("btnPrev") ? (Button)pnl.Controls["btnPrev"] : null;
                Button? btnPlay = pnl.Controls.ContainsKey("btnPlay") ? (Button)pnl.Controls["btnPlay"] : null;
                Button? btnNext = pnl.Controls.ContainsKey("btnNext") ? (Button)pnl.Controls["btnNext"] : null;

                float vol = (i == 0) ? dispositivoAudio.AudioEndpointVolume.MasterVolumeLevelScalar : canalesActuales[i - 1].Volumen;
                bool mut = (i == 0) ? dispositivoAudio.AudioEndpointVolume.Mute : canalesActuales[i - 1].Mute;

                pnlBarra.Width = (int)(vol * 330);
                pnlBarra.BackColor = mut ? Color.FromArgb(60, 60, 60) : (i == 0 ? Color.DeepSkyBlue : Color.MediumSpringGreen);
                btnMute.BackColor = mut ? Color.FromArgb(232, 17, 35) : Color.FromArgb(60, 60, 65);
                btnMute.ForeColor = mut ? Color.White : Color.Gray;
                if (btnSolo != null)
                {
                    bool esEsteElSolo = modoSoloActivado && indiceCanalSolo == i;
                    btnSolo.BackColor = esEsteElSolo ? Color.Gold : Color.FromArgb(60, 60, 65);
                    btnSolo.ForeColor = esEsteElSolo ? Color.Black : Color.Gray;
                }

                if (btnHide != null)
                {
                    bool estaOculto = memoriaHideApp.TryGetValue(canalesActuales[i - 1].Nombre, out bool h) && h;
                    btnHide.BackColor = estaOculto ? Color.DarkCyan : Color.FromArgb(60, 60, 65);
                    btnHide.ForeColor = estaOculto ? Color.White : Color.Gray;
                }

                if (i > 0 && btnPrev != null && btnPlay != null && btnNext != null)
                {
                    string nombreApp = canalesActuales[i - 1].Nombre;
                    bool tieneSesionMultimedia = infoMediosGlobal.ContainsKey(nombreApp);
                    btnPrev.Visible = tieneSesionMultimedia;
                    btnPlay.Visible = tieneSesionMultimedia;
                    btnNext.Visible = tieneSesionMultimedia;
                }

                pnl.BackColor = (i == indiceCanalActual) ? Color.FromArgb(45, 45, 50) : Color.FromArgb(35, 35, 40);
            }
        }

        private Button CrearBotonMedia(string nombre, string icono, int x)
        {
            Button btn = new Button
            {
                Name = nombre,
                Text = icono,
                Size = new Size(28, 22),
                Location = new Point(x, 7),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Emoji", 8, FontStyle.Regular),
                Cursor = Cursors.Hand,
                BackColor = Color.FromArgb(35, 35, 40),
                ForeColor = Color.Gray,
                Visible = false
            };

            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = Color.FromArgb(55, 55, 60);
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 120, 215);

            btn.MouseEnter += (s, e) => btn.ForeColor = Color.White;
            btn.MouseLeave += (s, e) => btn.ForeColor = Color.Gray;

            return btn;
        }

        private Panel CrearPanelUI(string nombre, Image? icono, int idIndice)
        {
            Panel p = new Panel { Width = 395, Height = 65, Margin = new Padding(0, 0, 0, 10), Tag = idIndice, Cursor = Cursors.Hand };
            PictureBox picIcono = new PictureBox { Size = new Size(32, 32), Location = new Point(10, 15), SizeMode = PictureBoxSizeMode.StretchImage, Image = icono ?? SystemIcons.Application.ToBitmap(), Cursor = Cursors.Hand };
            if (idIndice == 0) picIcono.Image = this.Icon?.ToBitmap() ?? SystemIcons.Shield.ToBitmap();

            Label n = new Label { Text = nombre, ForeColor = Color.White, Font = new Font("Segoe UI", 8, FontStyle.Bold), Location = new Point(50, 10), AutoSize = true, Cursor = Cursors.Hand };
            Action seleccionar = () => { indiceCanalActual = idIndice; ultimoGiroVolumen = DateTime.Now; ActualizarExterior(); };

            Button btnMute = new Button { Name = "btnMute", Text = "MUTE", Size = new Size(50, 20), Location = new Point(330, 8), FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 6, FontStyle.Bold), Cursor = Cursors.Hand, Tag = idIndice };
            btnMute.FlatAppearance.BorderSize = 0;

            Button? btnSolo = null;
            if (idIndice > 0) { btnSolo = new Button { Name = "btnSolo", Text = "SOLO", Size = new Size(50, 20), Location = new Point(275, 8), FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 6, FontStyle.Bold), Cursor = Cursors.Hand, Tag = idIndice }; btnSolo.FlatAppearance.BorderSize = 0; btnSolo.Click += (s, e) => { seleccionar(); AlternarSolo(idIndice); }; }

            Button? btnHide = null;
            if (idIndice > 0) { btnHide = new Button { Name = "btnHide", Text = "HIDE", Size = new Size(50, 20), Location = new Point(220, 8), FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 6, FontStyle.Bold), Cursor = Cursors.Hand, Tag = idIndice }; btnHide.FlatAppearance.BorderSize = 0; btnHide.Click += (s, e) => { ToggleHide(idIndice); }; }

            Button btnNext = CrearBotonMedia("btnNext", "⏭", 188);
            btnNext.Click += (s, e) => { seleccionar(); EjecutarAccionMultimediaNativa("NEXT", nombre); };

            Button btnPlay = CrearBotonMedia("btnPlay", "⏯", 156);
            btnPlay.Click += (s, e) => { seleccionar(); EjecutarAccionMultimediaNativa("PLAY_PAUSE", nombre); };

            Button btnPrev = CrearBotonMedia("btnPrev", "⏮", 124);
            btnPrev.Click += (s, e) => { seleccionar(); EjecutarAccionMultimediaNativa("PREV", nombre); };

            Panel f = new Panel { Name = "pnlFondo", Width = 330, Height = 14, BackColor = Color.Black, Location = new Point(50, 35), Cursor = Cursors.Hand, Tag = idIndice };
            Panel v = new Panel { Name = "pnlVol", Width = 0, Height = 14, BackColor = Color.White, Enabled = false };

            p.Click += (s, e) => seleccionar(); picIcono.Click += (s, e) => seleccionar(); n.Click += (s, e) => seleccionar(); btnMute.Click += (s, e) => { seleccionar(); ToggleMute(idIndice); };

            MouseEventHandler ajustarVolumen = (s, e) => {
                if (e.Button == MouseButtons.Left)
                {
                    seleccionar();
                    float nuevoVol = Math.Clamp((float)e.X / f.Width, 0, 1);
                    if (idIndice == 0)
                    {
                        dispositivoAudio!.AudioEndpointVolume.MasterVolumeLevelScalar = nuevoVol;
                    }
                    else
                    {
                        var canal = canalesActuales[idIndice - 1];
                        canal.Volumen = nuevoVol;
                        memoriaVolumenApp[canal.Nombre] = nuevoVol;
                    }
                    ultimoGiroVolumen = DateTime.Now;
                    ActualizarExterior();
                }
            };

            f.MouseDown += ajustarVolumen; f.MouseMove += ajustarVolumen; f.Controls.Add(v); p.Controls.Add(picIcono); p.Controls.Add(n); p.Controls.Add(btnMute);

            if (btnSolo != null) p.Controls.Add(btnSolo);
            if (btnHide != null) p.Controls.Add(btnHide);
            if (idIndice > 0)
            {
                p.Controls.Add(btnNext);
                p.Controls.Add(btnPlay);
                p.Controls.Add(btnPrev);
            }

            p.Controls.Add(f); return p;
        }

        private void AlternarSolo(int index)
        {
            if (dispositivoAudio == null || index == 0) return;
            if (modoSoloActivado && indiceCanalSolo == index) { modoSoloActivado = false; indiceCanalSolo = -1; foreach (var a in canalesActuales) { a.Mute = false; memoriaMuteApp[a.Nombre] = false; } }
            else
            {
                modoSoloActivado = true; indiceCanalSolo = index;
                if (dispositivoAudio.AudioEndpointVolume.Mute) { dispositivoAudio.AudioEndpointVolume.Mute = false; masterMuteActivado = false; }
                for (int i = 0; i < canalesActuales.Count; i++)
                {
                    bool debeMutear = (i + 1 != index);
                    canalesActuales[i].Mute = debeMutear;
                    memoriaMuteApp[canalesActuales[i].Nombre] = debeMutear;
                }
            }
            ActualizarDashboardPC(); ActualizarExterior();
        }

        private void ToggleHide(int index)
        {
            if (index <= 0 || index > canalesActuales.Count) return;
            var canal = canalesActuales[index - 1];
            
            bool currentHide = memoriaHideApp.TryGetValue(canal.Nombre, out bool h) && h;
            memoriaHideApp[canal.Nombre] = !currentHide;

            if (memoriaHideApp[canal.Nombre] && indiceCanalActual == index)
            {
                indiceCanalActual = 0;
                ActualizarExterior();
            }
            ActualizarDashboardPC();
        }

        private void ToggleMute(int index)
        {
            if (dispositivoAudio == null) return;
            if (masterMuteActivado && index != 0) { ActualizarExterior(); return; }
            if (index == 0)
            {
                bool nuevoEstadoMaster = !dispositivoAudio.AudioEndpointVolume.Mute;
                dispositivoAudio.AudioEndpointVolume.Mute = nuevoEstadoMaster;
                masterMuteActivado = nuevoEstadoMaster;
                if (masterMuteActivado)
                {
                    appsMuteadasAntesDelMaster.Clear();
                    foreach (var a in canalesActuales) { if (a.Mute) appsMuteadasAntesDelMaster.Add(a.Nombre); else { a.Mute = true; memoriaMuteApp[a.Nombre] = true; } }
                }
                else
                {
                    foreach (var a in canalesActuales) { if (!appsMuteadasAntesDelMaster.Contains(a.Nombre)) { a.Mute = false; memoriaMuteApp[a.Nombre] = false; } }
                    appsMuteadasAntesDelMaster.Clear();
                }
            }
            else if (index > 0 && index <= canalesActuales.Count)
            {
                var canal = canalesActuales[index - 1];
                canal.Mute = !canal.Mute;
                memoriaMuteApp[canal.Nombre] = canal.Mute;
            }
            ActualizarExterior();
        }

        private void DesmutearTodo()
        {
            if (dispositivoAudio == null) return;
            if (modoSoloActivado) { modoSoloActivado = false; indiceCanalSolo = -1; }
            dispositivoAudio.AudioEndpointVolume.Mute = false; masterMuteActivado = false; appsMuteadasAntesDelMaster.Clear();
            foreach (var a in canalesActuales) { a.Mute = false; memoriaMuteApp[a.Nombre] = false; }
            indiceCanalActual = 0; ActualizarExterior();
        }

        private void ProcesarComando(string cmd)
        {
            if (dispositivoAudio == null) return;

            if (cmd.StartsWith("MIXER32_OK")) { ultimaRespuestaPing = DateTime.Now; return; }

            if (cmd == "UP" || cmd == "DOWN") { if ((DateTime.Now - ultimoGiroEncoder).TotalMilliseconds < 2) return; ultimoGiroEncoder = DateTime.Now; ultimoGiroVolumen = DateTime.Now; }
            else { if ((DateTime.Now - ultimoClickBoton).TotalMilliseconds < 300) return; ultimoClickBoton = DateTime.Now; }

            int total = canalesActuales.Count + 1;

            if (cmd == "B1_LONG") { DesmutearTodo(); }
            else if (cmd == "BSW_SHORT") { if (indiceCanalActual == 0) ToggleMute(0); else ToggleMute(indiceCanalActual); }
            else if (cmd == "BSW_SOLO") { if (indiceCanalActual == 0) ToggleMute(0); else AlternarSolo(indiceCanalActual); }
            else if (cmd == "B1") { ToggleMute(0); indiceCanalActual = 0; }
            else if (cmd == "B2")
            {
                do {
                    indiceCanalActual = (indiceCanalActual + 1) % total;
                } while (indiceCanalActual > 0 && memoriaHideApp.TryGetValue(canalesActuales[indiceCanalActual - 1].Nombre, out bool h) && h);
            }
            else if (cmd == "B3")
            {
                do {
                    indiceCanalActual = (indiceCanalActual - 1 + total) % total;
                } while (indiceCanalActual > 0 && memoriaHideApp.TryGetValue(canalesActuales[indiceCanalActual - 1].Nombre, out bool h) && h);
            }

            else if (cmd == "MEDIA_NEXT") EjecutarAccionMultimediaNativa("NEXT");
            else if (cmd == "MEDIA_PREV") EjecutarAccionMultimediaNativa("PREV");
            else if (cmd == "MEDIA_PLAY_PAUSE") EjecutarAccionMultimediaNativa("PLAY_PAUSE");

            else if (cmd == "UP" || cmd == "DOWN")
            {
                float paso = 0.04f;
                if (indiceCanalActual == 0)
                {
                    dispositivoAudio.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(dispositivoAudio.AudioEndpointVolume.MasterVolumeLevelScalar + (cmd == "UP" ? paso : -paso), 0, 1);
                }
                else if (indiceCanalActual <= canalesActuales.Count)
                {
                    var canal = canalesActuales[indiceCanalActual - 1];
                    float nuevoVol = Math.Clamp(canal.Volumen + (cmd == "UP" ? paso : -paso), 0, 1);
                    canal.Volumen = nuevoVol;
                    memoriaVolumenApp[canal.Nombre] = nuevoVol;
                }
            }
            ActualizarExterior();
        }

        private void ActualizarExterior(bool forzarEnvio = false)
        {
            if (puertoExterior == null || !puertoExterior.IsOpen) return;
            try
            {
                string n; int v; bool mReal;
                if (indiceCanalActual == 0) { n = "MASTER"; v = (int)(dispositivoAudio!.AudioEndpointVolume.MasterVolumeLevelScalar * 100); mReal = dispositivoAudio.AudioEndpointVolume.Mute; }
                else { if (indiceCanalActual > canalesActuales.Count) indiceCanalActual = 0; var a = canalesActuales[indiceCanalActual - 1]; n = a.Nombre; v = (int)(a.Volumen * 100); mReal = a.Mute; }

                string artistaAMandar = "";

                if (indiceCanalActual > 0)
                {
                    if (infoMediosGlobal.TryGetValue(n, out InfoMedia mediaApp))
                    {
                        if (mediaApp.Reproduciendo && !string.IsNullOrEmpty(mediaApp.Titulo))
                        {
                            n = mediaApp.Titulo;
                            artistaAMandar = mediaApp.Artista;
                        }
                    }
                }

                bool enviarMute = mReal;
                if (mReal && (DateTime.Now - ultimoGiroVolumen).TotalMilliseconds < 1200) { enviarMute = false; exteriorMostrandoVolumenTemporal = true; } else { exteriorMostrandoVolumenTemporal = false; }

                bool enviarSolo = false;
                if (modoSoloActivado) { if (indiceCanalActual == indiceCanalSolo || indiceCanalActual == 0) { enviarSolo = true; } }

                bool hayOcultas = false;
                foreach (var kvp in memoriaHideApp) { if (kvp.Value) { hayOcultas = true; break; } }

                string mensajeAEnviar = $"N:{n.ToUpper()}\nA:{artistaAMandar.ToUpper()}\nV:{v}\nM:{(enviarMute ? "1" : "0")}\nS:{(enviarSolo ? "1" : "0")}\nH:{(hayOcultas ? "1" : "0")}\nT:{temaActualVisual}\nE:{(modoEcoActivado ? "1" : "0")}";

                if (mensajeAEnviar != ultimoMensajeExterior || forzarEnvio)
                {
                    puertoExterior.WriteLine(mensajeAEnviar);
                    ultimoMensajeExterior = mensajeAEnviar;
                }
            }
            catch { ManejarDesconexion(); }
        }

        private void ManejarDesconexion() { CerrarPuertoSeguro(); this.BeginInvoke(new Action(() => { if (lblEstadoConexion != null) { lblEstadoConexion.Text = "CONEXIÓN PERDIDA. RECONECTANDO..."; lblEstadoConexion.ForeColor = Color.Red; } })); ReiniciarBusqueda(); }
        private void CerrarPuertoSeguro() { try { if (puertoExterior != null) { if (puertoExterior.IsOpen) puertoExterior.Close(); puertoExterior.Dispose(); puertoExterior = null; } } catch { puertoExterior = null; } }
        private void ConfigurarBandejaSistema()
        {
            menuBandeja = new ContextMenuStrip();
            menuBandeja.Items.Add("Abrir Dashboard", null, (s, e) => MostrarAplicacion());
            menuBandeja.Items.Add("Info", null, (s, e) => MostrarInfo());
            menuBandeja.Items.Add("-");
            menuBandeja.Items.Add("Cerrar Linker32", null, (s, e) => { iconoBandeja!.Visible = false; Environment.Exit(0); });

            iconoBandeja = new NotifyIcon { Icon = this.Icon ?? SystemIcons.Application, ContextMenuStrip = menuBandeja, Text = "Linker32", Visible = true };
            iconoBandeja.DoubleClick += (s, e) => MostrarAplicacion();
        }
        private void MostrarInfo()
        {
            Form formInfo = new Form
            {
                Text = "Acerca de Linker32",
                Size = new Size(350, 220),
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Color.FromArgb(24, 24, 28),
                ForeColor = Color.White,
                ShowIcon = false
            };

            Label lblTitulo = new Label { Text = "LINKER32 • MIXER32", Font = new Font("Segoe UI", 12, FontStyle.Bold), ForeColor = Color.DeepSkyBlue, Location = new Point(20, 20), AutoSize = true };
            Label lblPC = new Label { Text = $"Versión Linker32 (PC):  v{VERSION_ACTUAL_PC}", Font = new Font("Segoe UI", 9, FontStyle.Bold), Location = new Point(20, 60), AutoSize = true };
            Label lblHW = new Label { Text = $"Versión Mixer32 (HW): v{VERSION_HARDWARE_INCLUIDA}", Font = new Font("Segoe UI", 9, FontStyle.Bold), Location = new Point(20, 85), AutoSize = true };
            Label lblAutor = new Label { Text = "Hardware y Software creado por: Rober Ben", Font = new Font("Segoe UI", 9, FontStyle.Regular), ForeColor = Color.LightGray, Location = new Point(20, 120), AutoSize = true };

            LinkLabel lnkGit = new LinkLabel
            {
                Text = "🔗 Visitar repositorio en GitHub",
                Location = new Point(20, 145),
                AutoSize = true,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                LinkColor = Color.MediumSpringGreen,
                ActiveLinkColor = Color.White,
                Cursor = Cursors.Hand
            };

            lnkGit.LinkClicked += (s, e) => {
                try
                {
                    Process.Start(new ProcessStartInfo("https://github.com/roberben/Linker32") { UseShellExecute = true });
                }
                catch { MessageBox.Show("No se pudo abrir el enlace.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };

            formInfo.Controls.Add(lblTitulo);
            formInfo.Controls.Add(lblPC);
            formInfo.Controls.Add(lblHW);
            formInfo.Controls.Add(lblAutor);
            formInfo.Controls.Add(lnkGit);

            formInfo.TopMost = true;
            formInfo.ShowDialog();
        }

        private void MostrarAplicacion() { this.Visible = true; this.Show(); this.WindowState = FormWindowState.Normal; this.BringToFront(); this.Activate(); }

        // --- NUEVO: SISTEMA DE ARRANQUE EN EL REGISTRO (Solución .dll / .exe) ---
        private void ConfigurarInicioAutomatico(bool activar)
        {
            try
            {
                string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(path, true)!)
                {
                    if (activar)
                    {
                        string rutaExe = Environment.ProcessPath ?? Application.ExecutablePath;
                        key.SetValue("Linker32Audio", $"\"{rutaExe}\" -hidden");
                    }
                    else
                    {
                        key.DeleteValue("Linker32Audio", false);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al configurar el inicio automático:\n" + ex.Message, "Error de Permisos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private bool ComprobarInicioAutomatico() { try { string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; using (RegistryKey key = Registry.CurrentUser.OpenSubKey(path, false)!) return key?.GetValue("Linker32Audio") != null; } catch { return false; } }
        private void GuardarEstadoAutoFoco(bool estado) { try { using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Linker32")) { key.SetValue("AutoFoco", estado ? 1 : 0); } } catch { } }
        private bool LeerEstadoAutoFoco() { try { using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Linker32")) { if (key != null) { object val = key.GetValue("AutoFoco"); return val != null && (int)val == 1; } } } catch { } return false; }
        private void GuardarEstadoTheme(int tema) { try { using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Linker32")) { key.SetValue("TemaOLED", tema); } } catch { } }
        private int LeerEstadoTema() { try { using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Linker32")) { if (key != null) { object val = key.GetValue("TemaOLED"); if (val != null) return (int)val; } } } catch { } return 3; }
        private void GuardarEstadoEco(bool estado) { try { using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Linker32")) { key.SetValue("ModoEco", estado ? 1 : 0); } } catch { } }
        private bool LeerEstadoEco() { try { using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Linker32")) { if (key != null) { object val = key.GetValue("ModoEco"); return val != null ? (int)val == 1 : true; } } } catch { } return true; }
    }
}