using System.Drawing.Drawing2D;
using System.Text.Json;
using NetInspector.Core;

namespace NetInspector.Desktop;

public sealed class DiskDialog : Form
{
    private readonly ComboBox drives = new ThemeComboBox() { Width = 88 };
    private readonly ComboBox action = new ThemeComboBox() { Width = 350 };
    private readonly Label media = new() { Dock = DockStyle.Top, Height = 54, ForeColor = Theme.Good, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button run = Theme.Action("Executar", Theme.Blue);
    private readonly Button close = Theme.Action("Fechar", Theme.Surface);
    private readonly TextBox output = new() { Dock = DockStyle.Bottom, Height = 190, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Theme.Background, ForeColor = Theme.Text };
    private readonly AllocationOrbit orbit = new() { Dock = DockStyle.Fill };
    private CancellationTokenSource? cancellation;
    public DiskDialog(bool freeSpace = false)
    {
        Theme.Apply(this, "Ferramentas • Saúde e espaço do disco"); MinimumSize = new(950, 750); Size = new(1080, 850);
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed && d.Name.Length == 3)) drives.Items.Add(drive.Name[..2]);
        if (drives.Items.Count > 0) drives.SelectedIndex = 0;
        action.Items.AddRange(["Analisar e mapear alocação", "Otimizar conforme a mídia (SSD/HDD)", "SSD: liberar áreas livres com TRIM", "HDD: sobrescrever espaço livre (NTFS)"]);
        action.SelectedIndex = freeSpace ? 2 : 0;
        var controls = new ControlRow { Dock = DockStyle.Top, AutoSize = true, Padding = new(0, 12, 0, 12) };
        run.Width = close.Width = 150;
        controls.Controls.AddRange([drives, action, run, close]);
        var help = new Label { Dock = DockStyle.Top, Height = 128, Padding = new(14), Text = "Órbitas de alocação: cada arco reúne clusters reais da amostra inicial do volume; verde indica espaço livre e azul, ocupado. A animação indica atividade, sem simular setores sendo movidos. Otimizar deixa o Windows escolher desfragmentação ou TRIM. Sobrescrita exige HDD confirmado, pode levar horas e ocupar temporariamente o espaço livre; TRIM não garante apagamento forense." };
        Controls.Add(orbit); Controls.Add(output); Controls.Add(media); Controls.Add(controls); Controls.Add(help);
        Theme.ToolWindow(this, "Saúde do armazenamento", help);
        DetectMedia();
        drives.SelectedIndexChanged += (_, _) => DetectMedia();
        Shown += (_, _) => { DetectMedia(); if (freeSpace && drives.SelectedItem is string volume) { var detected = StorageMedia.Detect(volume); action.SelectedIndex = detected.CanOverwrite ? 3 : detected.TrimSupported ? 2 : 1; } };
        run.Click += async (_, _) => await Execute();
        close.Click += (_, _) => { if (cancellation == null) Close(); else cancellation.Cancel(); };
        FormClosing += (_, e) => { if (cancellation != null) { e.Cancel = true; cancellation.Cancel(); } };
    }
    private void DetectMedia()
    {
        if (drives.SelectedItem is not string volume) { run.Enabled = false; media.Text = "Nenhuma unidade fixa disponível."; return; }
        var detected = StorageMedia.Detect(volume);
        media.Text = $"{volume}  •  {detected.Kind}  /  {detected.Evidence}";
        if (action.SelectedIndex == 2 && !detected.TrimSupported) action.SelectedIndex = 1;
        if (action.SelectedIndex == 3 && !detected.CanOverwrite) action.SelectedIndex = 1;
    }
    private void Append(string text)
    { if (output.TextLength > 100_000) output.Text = output.Text[^60_000..]; output.AppendText(text + Environment.NewLine); }
    private async Task Execute()
    {
        if (drives.SelectedItem is not string volume || volume.Length != 2 || !char.IsAsciiLetter(volume[0]) || volume[1] != ':') return;
        var mode = action.SelectedIndex;
        var detected = StorageMedia.Detect(volume);
        if (mode == 3 && !detected.CanOverwrite) { Append("Sobrescrita bloqueada: HDD físico não identificado com segurança. " + detected.Evidence); return; }
        if (mode == 3 && MessageBox.Show(this, "HDD detectado automaticamente em " + volume + ". A sobrescrita do espaço livre pode levar horas e dificulta a recuperação de arquivos excluídos. Salve seu trabalho e mantenha o computador ligado à tomada.", "Confirmar sobrescrita", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        if (MessageBox.Show(this, action.Text + " na unidade " + volume + "?\r\nO Windows solicitará administrador. Salve seu trabalho.", "Confirmar disco", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        cancellation = new(); run.Enabled = drives.Enabled = action.Enabled = false; close.Text = "Cancelar"; orbit.Busy = true;
        var native = mode switch { 0 => $"Invoke-Native 'defrag.exe' '{volume} /A /U /V'", 1 => $"Invoke-Native 'defrag.exe' '{volume} /O /U /V'", 2 => $"if ([VolumeBitmap]::Property($volume,8) -ne 1) {{ throw 'TRIM não confirmado pelo dispositivo. Use Otimizar conforme a mídia.' }}; Invoke-Native 'defrag.exe' '{volume} /L /U /V'", _ => $"if (-not [VolumeBitmap]::PhysicalHdd($volume)) {{ throw 'HDD físico não confirmado pelo dispositivo. Sobrescrita bloqueada.' }}; if ((Get-Volume -DriveLetter '{volume[0]}').FileSystem -ne 'NTFS') {{ throw 'Sobrescrita disponível somente para NTFS.' }}; Invoke-Native 'cipher.exe' '/w:{volume}\\'" };
        var script = MaintenanceRunner.NativeFunction + "\n$volume=" + MaintenanceRunner.Quote(volume) + "\n" + BitmapScript + "\nWrite-Output 'Iniciando operação do Windows...';\n" + native + "\nif (-not (Test-Path -LiteralPath $cancelFile)) { Read-Allocation }";
        try
        {
            var code = await MaintenanceRunner.Run(script, true, text =>
            {
                foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    if (line.TrimStart().StartsWith("MAP:")) { orbit.Values = JsonSerializer.Deserialize<double[]>(line.Trim()[4..]); orbit.Invalidate(); }
                    else Append(line);
            }, cancellation.Token);
            Append(code == 0 ? "Operação concluída pelo Windows. Mapa atualizado quando disponível." : "Operação não concluída; consulte o resultado acima.");
        }
        catch (Exception ex) { Append("Falha: " + ex.Message); }
        finally { cancellation.Dispose(); cancellation = null; run.Enabled = drives.Enabled = action.Enabled = true; close.Text = "Fechar"; orbit.Busy = false; }
    }
    private const string BitmapScript = """
        Add-Type -TypeDefinition @'
        using System;
        using System.Runtime.InteropServices;
        using Microsoft.Win32.SafeHandles;
        public static class VolumeBitmap {
            [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
            public static extern SafeFileHandle CreateFile(string n, uint a, uint s, IntPtr p, uint c, uint f, IntPtr t);
            [DllImport("kernel32.dll", SetLastError=true)]
            public static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] input, int ins, byte[] output, int outs, out int returned, IntPtr overlapped);
            public static int Property(string drive, int property) {
                using (var handle=CreateFile(@"\\.\"+drive, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero)) {
                    if(handle.IsInvalid) return -1;
                    var input=new byte[12]; Array.Copy(BitConverter.GetBytes(property),input,4);
                    var output=new byte[16]; int count;
                    if(!DeviceIoControl(handle,0x2D1400,input,input.Length,output,output.Length,out count,IntPtr.Zero) || count<9) return -1;
                    return output[8]==0?0:1;
                }
            }
            public static bool PhysicalHdd(string drive) {
                using (var handle=CreateFile(@"\\.\"+drive, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero)) {
                    if(handle.IsInvalid) return false;
                    var extents=new byte[1024]; int count;
                    if(!DeviceIoControl(handle,0x560000,new byte[0],0,extents,extents.Length,out count,IntPtr.Zero) || count<4 || BitConverter.ToInt32(extents,0)!=1) return false;
                    var input=new byte[12]; var descriptor=new byte[1024];
                    if(!DeviceIoControl(handle,0x2D1400,input,input.Length,descriptor,descriptor.Length,out count,IntPtr.Zero) || count<32) return false;
                    int bus=BitConverter.ToInt32(descriptor,28);
                    if(bus!=1 && bus!=3 && bus!=10 && bus!=11 && bus!=19) return false;
                    if(Property(drive,11)==1) return false;
                    return Property(drive,7)==1;
                }
            }
            public static double[] Read(string drive) {
                using (var handle=CreateFile(@"\\.\"+drive, 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero)) {
                    if(handle.IsInvalid) throw new System.ComponentModel.Win32Exception();
                    var buffer=new byte[1048592]; int count;
                    bool ok=DeviceIoControl(handle,0x9006f,new byte[8],8,buffer,buffer.Length,out count,IntPtr.Zero);
                    if(!ok && Marshal.GetLastWin32Error()!=234) throw new System.ComponentModel.Win32Exception();
                    long bits=Math.Min(BitConverter.ToInt64(buffer,8),(long)(count-16)*8);
                    if(bits<=0) throw new InvalidOperationException("Bitmap vazio");
                    var values=new double[240]; var totals=new long[240];
                    for(long i=0;i<bits;i++) { int b=(int)(i*240/bits); totals[b]++; if((buffer[16+(int)(i/8)] & (1 << (int)(i%8)))!=0) values[b]++; }
                    for(int i=0;i<240;i++) values[i]=totals[i]==0?0:values[i]/totals[i];
                    return values;
                }
            }
        }
        '@
        function Read-Allocation {
            try { Write-Output ('MAP:' + (ConvertTo-Json -InputObject ([VolumeBitmap]::Read($volume)) -Compress)) }
            catch { Write-Output ('Mapa de alocação indisponível: ' + $_) }
        }
        Read-Allocation
        """;
}

public sealed class AllocationOrbit : Control
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 50 };
    private int phase;
    private bool busy;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double[]? Values { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Busy { get => busy; set { busy = value; timer.Enabled = value; Invalidate(); } }
    public AllocationOrbit()
    {
        DoubleBuffered = true; ResizeRedraw = true;
        AccessibleName = "Mapa orbital de alocação: amostra de até 8.388.608 clusters iniciais, agrupados em 240 arcos. Não mede fragmentação.";
        timer.Tick += (_, _) => { phase = (phase + 3) % 360; Invalidate(); };
    }
    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var size = Math.Min(Width - 100, Height - 80); if (size < 120) return;
        var center = new PointF(Width / 2f, Height / 2f - 8);
        for (var ring = 0; ring < 4; ring++)
        {
            var radius = size / 2f - ring * size / 12f;
            for (var sector = 0; sector < 60; sector++)
            {
                var index = ring * 60 + sector;
                var value = Values is { Length: 240 } ? Values[index] : -1;
                var color = value < 0 ? Theme.Border : Color.FromArgb((int)(Theme.Good.R + (Theme.Blue.R - Theme.Good.R) * value), (int)(Theme.Good.G + (Theme.Blue.G - Theme.Good.G) * value), (int)(Theme.Good.B + (Theme.Blue.B - Theme.Good.B) * value));
                using var pen = new Pen(color, Math.Max(4, size / 30f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawArc(pen, center.X - radius, center.Y - radius, radius * 2, radius * 2, sector * 6 - 90, 3.7f);
            }
        }
        if (Busy) { using var pen = new Pen(Theme.Orange, 3); var r = size / 2f + 12; g.DrawArc(pen, center.X-r, center.Y-r, r*2, r*2, phase, 40); }
        TextRenderer.DrawText(g, Busy ? "EM ATIVIDADE" : Values == null ? "AGUARDANDO\nANÁLISE" : "ALOCAÇÃO\nREAL", Font, new Rectangle((int)center.X-75, (int)center.Y-30, 150, 60), Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "Amostra inicial • até 8.388.608 clusters • livre → ocupado • não representa fragmentação", Font, new Rectangle(0, Height - 28, Width, 28), Theme.Muted, TextFormatFlags.HorizontalCenter);
    }
}
