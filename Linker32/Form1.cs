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

namespace Linker32
{
    public partial class Form1 : Form
    {
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

        private SerialPort? puertoExterior;
        private MMDevice? dispositivoAudio;
        private int indiceCanalActual = 0;
        private List<AudioSessionControl> sesionesActuales = new List<AudioSessionControl>();
        private bool buscandoHardware = false;
        private CancellationTokenSource? ctsBusqueda;

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

        private DateTime ultimaRespuestaPing = DateTime.Now;
        private DateTime ultimoEnvioPing = DateTime.MinValue;

        // --- NUEVA VARIABLE DE TEMA ---
        private int temaActualVisual = 3;

        private NotifyIcon? iconoBandeja;
        private ContextMenuStrip? menuBandeja;
        private FlowLayoutPanel? panelContenedor;
        private System.Windows.Forms.Timer? timerRefrescoUI;
        private Label? lblEstadoConexion;
        private CheckBox? chkInicioAuto;
        private CheckBox? chkAutoFoco;

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

            ReiniciarBusqueda();
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

            pnlHeader.Controls.Add(lblTitulo); pnlHeader.Controls.Add(lblEstadoConexion); pnlHeader.Controls.Add(btnCerrar);

            Panel pnlFooter = new Panel { Dock = DockStyle.Bottom, Height = 45, BackColor = Color.FromArgb(20, 20, 22) };

            chkInicioAuto = new CheckBox { Text = "INICIAR AUTO", Font = new Font("Segoe UI", 7, FontStyle.Bold), Location = new Point(15, 12), AutoSize = true, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            chkInicioAuto.Checked = ComprobarInicioAutomatico();
            chkInicioAuto.ForeColor = chkInicioAuto.Checked ? Color.MediumSpringGreen : Color.DimGray;
            chkInicioAuto.CheckedChanged += (s, e) => { ConfigurarInicioAutomatico(chkInicioAuto.Checked); chkInicioAuto.ForeColor = chkInicioAuto.Checked ? Color.MediumSpringGreen : Color.DimGray; };

            chkAutoFoco = new CheckBox { Text = "AUTO-FOCO", Font = new Font("Segoe UI", 7, FontStyle.Bold), Location = new Point(125, 12), AutoSize = true, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            autoFocoActivado = LeerEstadoAutoFoco();
            chkAutoFoco.Checked = autoFocoActivado;
            chkAutoFoco.ForeColor = chkAutoFoco.Checked ? Color.DeepSkyBlue : Color.DimGray;
            chkAutoFoco.CheckedChanged += (s, e) => { autoFocoActivado = chkAutoFoco.Checked; GuardarEstadoAutoFoco(autoFocoActivado); chkAutoFoco.ForeColor = autoFocoActivado ? Color.DeepSkyBlue : Color.DimGray; };

            // --- BOTONES DE TEMAS ---
            btnTema1 = CrearBotonTema("ARC", 225, 1);
            btnTema2 = CrearBotonTema("CYB", 265, 2);
            btnTema3 = CrearBotonTema("PRO", 305, 3);
            ActualizarEstiloBotonesTema();

            Label lblFirma = new Label { Text = "BY ROBER BEN", ForeColor = Color.FromArgb(60, 60, 65), Font = new Font("Segoe UI", 8, FontStyle.Bold), AutoSize = true, Location = new Point(345, 14) };

            pnlFooter.Controls.Add(chkInicioAuto); pnlFooter.Controls.Add(chkAutoFoco);
            pnlFooter.Controls.Add(btnTema1); pnlFooter.Controls.Add(btnTema2); pnlFooter.Controls.Add(btnTema3);
            pnlFooter.Controls.Add(lblFirma);

            panelContenedor = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(15, 80, 15, 20), FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent };

            this.Controls.Add(panelContenedor); this.Controls.Add(pnlHeader); this.Controls.Add(pnlFooter);
            pnlHeader.BringToFront(); pnlFooter.BringToFront();

            timerRefrescoUI = new System.Windows.Forms.Timer { Interval = 100 };
            timerRefrescoUI.Tick += (s, e) => {
                ActualizarDashboardPC();
                ProcesarAutoFoco();

                if (puertoExterior == null || !puertoExterior.IsOpen) { ReiniciarBusqueda(); }
                else
                {
                    ActualizarExterior();
                    if ((DateTime.Now - ultimoEnvioPing).TotalSeconds > 2) { try { puertoExterior.WriteLine("PING"); } catch { ManejarDesconexion(); return; } ultimoEnvioPing = DateTime.Now; }
                    if ((DateTime.Now - ultimaRespuestaPing).TotalSeconds > 5) { ManejarDesconexion(); }
                }
            };
            timerRefrescoUI.Start();
        }

        private Button CrearBotonTema(string texto, int x, int temaNum)
        {
            Button btn = new Button { Text = texto, Size = new Size(35, 22), Location = new Point(x, 10), FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 6, FontStyle.Bold), Cursor = Cursors.Hand };
            btn.FlatAppearance.BorderSize = 0;
            btn.Click += (s, e) => { temaActualVisual = temaNum; GuardarEstadoTema(temaActualVisual); ActualizarEstiloBotonesTema(); ActualizarExterior(true); };
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
            if (!autoFocoActivado || sesionesActuales.Count == 0) return;
            IntPtr hwndFoco = GetForegroundWindow();
            if (hwndFoco != IntPtr.Zero)
            {
                uint pidFoco; GetWindowThreadProcessId(hwndFoco, out pidFoco);
                if (pidFoco != ultimoPIDFoco)
                {
                    ultimoPIDFoco = pidFoco; string nombreProcesoFoco = "";
                    try { using (Process proc = Process.GetProcessById((int)pidFoco)) { nombreProcesoFoco = proc.ProcessName; } } catch { return; }
                    for (int i = 0; i < sesionesActuales.Count; i++)
                    {
                        if (ObtenerNombreProceso(sesionesActuales[i]).Equals(nombreProcesoFoco, StringComparison.OrdinalIgnoreCase))
                        {
                            if (indiceCanalActual != i + 1) { indiceCanalActual = i + 1; ultimoGiroVolumen = DateTime.Now; ActualizarExterior(); }
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
            string arc = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "puerto_mixer32.txt");

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
                        if (tempPort.ReadLine().Trim() == "MIXER32_OK")
                        {
                            puertoExterior = tempPort;
                            puertoExterior.DataReceived += (s, e) => { try { if (puertoExterior != null && puertoExterior.IsOpen) { string cmd = puertoExterior.ReadLine().Trim(); this.BeginInvoke(new Action(() => ProcesarComando(cmd))); } } catch { ManejarDesconexion(); } };
                            File.WriteAllText(arc, p);
                            ultimaRespuestaPing = DateTime.Now; ultimoEnvioPing = DateTime.Now;
                            this.BeginInvoke(new Action(() => { lblEstadoConexion!.Text = $"CONECTADO EXTERIOR: {p}"; lblEstadoConexion.ForeColor = Color.MediumSpringGreen; ActualizarExterior(true); }));
                            buscandoHardware = false; return;
                        }
                    }
                    catch { if (tempPort != null) { if (tempPort.IsOpen) tempPort.Close(); tempPort.Dispose(); } }
                }
                this.BeginInvoke(new Action(() => { lblEstadoConexion!.Text = "BUSCANDO HARDWARE..."; lblEstadoConexion.ForeColor = Color.DarkOrange; }));
                await Task.Delay(3000, token);
            }
            buscandoHardware = false;
        }

        private void ActualizarDashboardPC()
        {
            if (dispositivoAudio == null || panelContenedor == null) return;
            sesionesActuales = ObtenerAplicacionesActivas();

            HashSet<uint> pidsActuales = new HashSet<uint>();
            foreach (var a in sesionesActuales) { uint pid = a.GetProcessID; pidsActuales.Add(pid); if (!procesosConocidos.Contains(pid)) { procesosConocidos.Add(pid); if (masterMuteActivado) a.SimpleAudioVolume.Mute = true; } }
            procesosConocidos.IntersectWith(pidsActuales);

            int total = sesionesActuales.Count + 1;
            if (panelContenedor.Controls.Count != total)
            {
                panelContenedor.Controls.Clear(); panelContenedor.Controls.Add(CrearPanelUI("MASTER", null, 0));
                for (int i = 0; i < sesionesActuales.Count; i++) panelContenedor.Controls.Add(CrearPanelUI(ObtenerNombreProceso(sesionesActuales[i]).ToUpper(), ExtraerIconoProceso((int)sesionesActuales[i].GetProcessID), i + 1));
            }

            for (int i = 0; i < panelContenedor.Controls.Count; i++)
            {
                Panel pnl = (Panel)panelContenedor.Controls[i];
                Panel pnlBarra = (Panel)pnl.Controls["pnlFondo"].Controls["pnlVol"];
                Button btnMute = (Button)pnl.Controls["btnMute"];
                Button? btnSolo = pnl.Controls.ContainsKey("btnSolo") ? (Button)pnl.Controls["btnSolo"] : null;

                float vol = (i == 0) ? dispositivoAudio.AudioEndpointVolume.MasterVolumeLevelScalar : sesionesActuales[i - 1].SimpleAudioVolume.Volume;
                bool mut = (i == 0) ? dispositivoAudio.AudioEndpointVolume.Mute : sesionesActuales[i - 1].SimpleAudioVolume.Mute;

                pnlBarra.Width = (int)(vol * 330); pnlBarra.BackColor = mut ? Color.FromArgb(60, 60, 60) : (i == 0 ? Color.DeepSkyBlue : Color.MediumSpringGreen);
                btnMute.BackColor = mut ? Color.FromArgb(232, 17, 35) : Color.FromArgb(60, 60, 65); btnMute.ForeColor = mut ? Color.White : Color.Gray;
                if (btnSolo != null) { bool esEsteElSolo = modoSoloActivado && indiceCanalSolo == i; btnSolo.BackColor = esEsteElSolo ? Color.Gold : Color.FromArgb(60, 60, 65); btnSolo.ForeColor = esEsteElSolo ? Color.Black : Color.Gray; }
                pnl.BackColor = (i == indiceCanalActual) ? Color.FromArgb(45, 45, 50) : Color.FromArgb(35, 35, 40);
            }
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

            Panel f = new Panel { Name = "pnlFondo", Width = 330, Height = 14, BackColor = Color.Black, Location = new Point(50, 35), Cursor = Cursors.Hand, Tag = idIndice };
            Panel v = new Panel { Name = "pnlVol", Width = 0, Height = 14, BackColor = Color.White, Enabled = false };

            p.Click += (s, e) => seleccionar(); picIcono.Click += (s, e) => seleccionar(); n.Click += (s, e) => seleccionar(); btnMute.Click += (s, e) => { seleccionar(); ToggleMute(idIndice); };
            MouseEventHandler ajustarVolumen = (s, e) => { if (e.Button == MouseButtons.Left) { seleccionar(); float nuevoVol = Math.Clamp((float)e.X / f.Width, 0, 1); if (idIndice == 0) dispositivoAudio!.AudioEndpointVolume.MasterVolumeLevelScalar = nuevoVol; else sesionesActuales[idIndice - 1].SimpleAudioVolume.Volume = nuevoVol; ultimoGiroVolumen = DateTime.Now; ActualizarExterior(); } };
            f.MouseDown += ajustarVolumen; f.MouseMove += ajustarVolumen; f.Controls.Add(v); p.Controls.Add(picIcono); p.Controls.Add(n); p.Controls.Add(btnMute); if (btnSolo != null) p.Controls.Add(btnSolo); p.Controls.Add(f); return p;
        }

        private void AlternarSolo(int index)
        {
            if (dispositivoAudio == null || index == 0) return;
            if (modoSoloActivado && indiceCanalSolo == index) { modoSoloActivado = false; indiceCanalSolo = -1; foreach (var a in sesionesActuales) a.SimpleAudioVolume.Mute = false; }
            else { modoSoloActivado = true; indiceCanalSolo = index; if (dispositivoAudio.AudioEndpointVolume.Mute) { dispositivoAudio.AudioEndpointVolume.Mute = false; masterMuteActivado = false; } for (int i = 0; i < sesionesActuales.Count; i++) sesionesActuales[i].SimpleAudioVolume.Mute = (i + 1 != index); }
            ActualizarDashboardPC(); ActualizarExterior();
        }

        private void ToggleMute(int index)
        {
            if (dispositivoAudio == null) return;
            if (masterMuteActivado && index != 0) { ActualizarExterior(); return; }
            if (index == 0) { bool nuevoEstadoMaster = !dispositivoAudio.AudioEndpointVolume.Mute; dispositivoAudio.AudioEndpointVolume.Mute = nuevoEstadoMaster; masterMuteActivado = nuevoEstadoMaster; if (masterMuteActivado) { appsMuteadasAntesDelMaster.Clear(); foreach (var a in sesionesActuales) { if (a.SimpleAudioVolume.Mute) appsMuteadasAntesDelMaster.Add(ObtenerNombreProceso(a)); else a.SimpleAudioVolume.Mute = true; } } else { foreach (var a in sesionesActuales) { if (!appsMuteadasAntesDelMaster.Contains(ObtenerNombreProceso(a))) a.SimpleAudioVolume.Mute = false; } appsMuteadasAntesDelMaster.Clear(); } }
            else if (index > 0 && index <= sesionesActuales.Count) { sesionesActuales[index - 1].SimpleAudioVolume.Mute = !sesionesActuales[index - 1].SimpleAudioVolume.Mute; }
            ActualizarExterior();
        }

        private void DesmutearTodo()
        {
            if (dispositivoAudio == null) return;
            if (modoSoloActivado) { modoSoloActivado = false; indiceCanalSolo = -1; }
            dispositivoAudio.AudioEndpointVolume.Mute = false; masterMuteActivado = false; appsMuteadasAntesDelMaster.Clear();
            foreach (var a in sesionesActuales) a.SimpleAudioVolume.Mute = false;
            indiceCanalActual = 0; ActualizarExterior();
        }

        private void ProcesarComando(string cmd)
        {
            if (dispositivoAudio == null) return;
            if (cmd == "MIXER32_OK") { ultimaRespuestaPing = DateTime.Now; return; }
            if (cmd == "UP" || cmd == "DOWN") { if ((DateTime.Now - ultimoGiroEncoder).TotalMilliseconds < 2) return; ultimoGiroEncoder = DateTime.Now; ultimoGiroVolumen = DateTime.Now; }
            else { if ((DateTime.Now - ultimoClickBoton).TotalMilliseconds < 300) return; ultimoClickBoton = DateTime.Now; }

            int total = sesionesActuales.Count + 1;
            if (cmd == "B1_LONG") { DesmutearTodo(); }
            else if (cmd == "BSW_SHORT") { if (indiceCanalActual == 0) ToggleMute(0); else ToggleMute(indiceCanalActual); }
            else if (cmd == "BSW_SOLO") { if (indiceCanalActual == 0) ToggleMute(0); else AlternarSolo(indiceCanalActual); }
            else if (cmd == "B1") { ToggleMute(0); indiceCanalActual = 0; }
            else if (cmd == "B2") indiceCanalActual = (indiceCanalActual + 1) % total;
            else if (cmd == "B3") indiceCanalActual = (indiceCanalActual - 1 + total) % total;
            else if (cmd == "UP" || cmd == "DOWN")
            {
                float paso = 0.04f;
                if (indiceCanalActual == 0) dispositivoAudio.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(dispositivoAudio.AudioEndpointVolume.MasterVolumeLevelScalar + (cmd == "UP" ? paso : -paso), 0, 1);
                else if (indiceCanalActual <= sesionesActuales.Count) sesionesActuales[indiceCanalActual - 1].SimpleAudioVolume.Volume = Math.Clamp(sesionesActuales[indiceCanalActual - 1].SimpleAudioVolume.Volume + (cmd == "UP" ? paso : -paso), 0, 1);
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
                else { if (indiceCanalActual > sesionesActuales.Count) indiceCanalActual = 0; var a = sesionesActuales[indiceCanalActual - 1]; n = ObtenerNombreProceso(a); v = (int)(a.SimpleAudioVolume.Volume * 100); mReal = a.SimpleAudioVolume.Mute; }

                bool enviarMute = mReal;
                if (mReal && (DateTime.Now - ultimoGiroVolumen).TotalMilliseconds < 1200) { enviarMute = false; exteriorMostrandoVolumenTemporal = true; } else { exteriorMostrandoVolumenTemporal = false; }

                bool enviarSolo = false;
                if (modoSoloActivado) { if (indiceCanalActual == indiceCanalSolo || indiceCanalActual == 0) { enviarSolo = true; } }

                // AHORA MANDAMOS TAMBIÉN EL TEMA VISUAL (T:x)
                string mensajeAEnviar = $"N:{n.ToUpper()}\nV:{v}\nM:{(enviarMute ? "1" : "0")}\nS:{(enviarSolo ? "1" : "0")}\nT:{temaActualVisual}";

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
        private Image? ExtraerIconoProceso(int pid) { try { using (Process proc = Process.GetProcessById(pid)) { string ruta = proc.MainModule?.FileName ?? ""; if (!string.IsNullOrEmpty(ruta)) return Icon.ExtractAssociatedIcon(ruta)?.ToBitmap(); } } catch { } return null; }
        private List<AudioSessionControl> ObtenerAplicacionesActivas() { var lista = new List<AudioSessionControl>(); if (dispositivoAudio == null) return lista; var manager = dispositivoAudio.AudioSessionManager; manager.RefreshSessions(); for (int i = 0; i < manager.Sessions.Count; i++) if (!manager.Sessions[i].IsSystemSoundsSession && manager.Sessions[i].GetProcessID != 0) lista.Add(manager.Sessions[i]); lista.Sort((a, b) => string.Compare(ObtenerNombreProceso(a), ObtenerNombreProceso(b), StringComparison.OrdinalIgnoreCase)); return lista; }
        private string ObtenerNombreProceso(AudioSessionControl s) { try { return Process.GetProcessById((int)s.GetProcessID).ProcessName; } catch { return "APP"; } }
        private void ConfigurarBandejaSistema() { menuBandeja = new ContextMenuStrip(); menuBandeja.Items.Add("Abrir Dashboard", null, (s, e) => MostrarAplicacion()); menuBandeja.Items.Add("Cerrar Linker32", null, (s, e) => { iconoBandeja!.Visible = false; Environment.Exit(0); }); iconoBandeja = new NotifyIcon { Icon = this.Icon ?? SystemIcons.Application, ContextMenuStrip = menuBandeja, Text = "Linker32", Visible = true }; iconoBandeja.DoubleClick += (s, e) => MostrarAplicacion(); }
        private void MostrarAplicacion() { this.Visible = true; this.Show(); this.WindowState = FormWindowState.Normal; this.BringToFront(); this.Activate(); }
        private void ConfigurarInicioAutomatico(bool activar) { try { string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; using (RegistryKey key = Registry.CurrentUser.OpenSubKey(path, true)!) { if (activar) key.SetValue("Linker32Audio", $"\"{Application.ExecutablePath}\" -hidden"); else key.DeleteValue("Linker32Audio", false); } } catch { } }
        private bool ComprobarInicioAutomatico() { try { string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; using (RegistryKey key = Registry.CurrentUser.OpenSubKey(path, false)!) return key?.GetValue("Linker32Audio") != null; } catch { return false; } }
        private void GuardarEstadoAutoFoco(bool estado) { try { using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Linker32")) { key.SetValue("AutoFoco", estado ? 1 : 0); } } catch { } }
        private bool LeerEstadoAutoFoco() { try { using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Linker32")) { if (key != null) { object val = key.GetValue("AutoFoco"); return val != null && (int)val == 1; } } } catch { } return false; }

        // --- GUARDAR Y LEER EL TEMA VISUAL ---
        private void GuardarEstadoTema(int tema) { try { using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Linker32")) { key.SetValue("TemaOLED", tema); } } catch { } }
        private int LeerEstadoTema() { try { using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Linker32")) { if (key != null) { object val = key.GetValue("TemaOLED"); if (val != null) return (int)val; } } } catch { } return 3; }
    }
}