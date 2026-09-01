using System.Diagnostics;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace FaceCompareTest;

/// <summary>
/// 人脸比对测试主窗体：选两张图片 → 新模型（detect/extract/quality）检测与特征比对 → 展示相似度与结论。
/// </summary>
public class MainForm : Form
{
    private readonly ComboBox _deviceCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly NumericUpDown _threshold = new() { Minimum = 0, Maximum = 100, Value = 30, Width = 70 };
    private readonly Button _compareBtn = new() { Text = "开始比对", Width = 110, Height = 34 };
    private readonly Label _status = new() { AutoSize = true, Text = "就绪" };
    private readonly PictureBox _pic1 = new() { SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
    private readonly PictureBox _pic2 = new() { SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
    private readonly RichTextBox _result = new() { ReadOnly = true, ScrollBars = RichTextBoxScrollBars.Vertical, Font = new Font("Microsoft YaHei UI", 10f) };

    private string? _path1, _path2;
    private FaceEngine? _engine;
    private string _engineDevice = string.Empty;
    private bool _busy;

    public MainForm()
    {
        Text = "人脸比对测试（新模型 · OpenVINO）";
        Size = new System.Drawing.Size(1080, 780);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9.5f);

        // ===== 顶部工具条 =====
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 56, Padding = new Padding(10, 10, 10, 4) };
        top.Controls.Add(new Label { Text = "推理设备：", AutoSize = true, Margin = new Padding(3, 10, 3, 3) });
        _deviceCombo.Items.AddRange(new object[] { "CPU", "GPU", "AUTO:GPU,CPU" });
        _deviceCombo.SelectedIndex = 0;
        top.Controls.Add(_deviceCombo);
        top.Controls.Add(new Label { Text = "通过阈值(%)：", AutoSize = true, Margin = new Padding(16, 10, 3, 3) });
        top.Controls.Add(_threshold);
        _compareBtn.Click += async (_, _) => await CompareAsync();
        top.Controls.Add(_compareBtn);
        _status.Margin = new Padding(16, 10, 3, 3);
        top.Controls.Add(_status);

        // ===== 中部：两张图片 =====
        var center = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(10) };
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        center.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var btn1 = new Button { Text = "选择图片 1", Dock = DockStyle.Fill };
        var btn2 = new Button { Text = "选择图片 2", Dock = DockStyle.Fill };
        btn1.Click += (_, _) => PickImage(1);
        btn2.Click += (_, _) => PickImage(2);
        center.Controls.Add(btn1, 0, 0);
        center.Controls.Add(btn2, 1, 0);
        _pic1.Dock = DockStyle.Fill; _pic2.Dock = DockStyle.Fill;
        center.Controls.Add(_pic1, 0, 1);
        center.Controls.Add(_pic2, 1, 1);

        // ===== 底部：结果 =====
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 430 };
        split.Panel1.Controls.Add(center);
        _result.Dock = DockStyle.Fill;
        split.Panel2.Controls.Add(_result);

        Controls.Add(split);
        Controls.Add(top);

        FormClosed += (_, _) => _engine?.Dispose();
    }

    private void PickImage(int slot)
    {
        using var dlg = new OpenFileDialog { Title = $"选择图片 {slot}", Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (slot == 1) { _path1 = dlg.FileName; ShowImage(_pic1, dlg.FileName); }
        else { _path2 = dlg.FileName; ShowImage(_pic2, dlg.FileName); }
    }

    private static void ShowImage(PictureBox box, string path)
    {
        using var mat = Cv2.ImRead(path);
        box.Image?.Dispose();
        box.Image = BitmapConverter.ToBitmap(mat);
    }

    /// <summary>在显示图上画人脸框与五点关键点。</summary>
    private static void ShowImageWithFace(PictureBox box, string path, FaceResult? face)
    {
        using var mat = Cv2.ImRead(path);
        if (face is not null)
        {
            Cv2.Rectangle(mat, face.Box, new Scalar(0, 255, 0), 2);
            foreach (var p in face.Landmarks)
                Cv2.Circle(mat, (int)p.X, (int)p.Y, 2, new Scalar(0, 0, 255), -1);
        }
        box.Image?.Dispose();
        box.Image = BitmapConverter.ToBitmap(mat);
    }

    private async Task CompareAsync()
    {
        if (_busy) return;
        if (_path1 is null || _path2 is null)
        {
            MessageBox.Show(this, "请先选择两张图片", "提示");
            return;
        }

        _busy = true;
        _compareBtn.Enabled = false;
        _deviceCombo.Enabled = false;
        var device = (string)_deviceCombo.SelectedItem!;
        var threshold = (float)_threshold.Value;
        var path1 = _path1; var path2 = _path2;
        _status.Text = "模型加载 / 推理中…";
        _result.Clear();

        try
        {
            var sw = Stopwatch.StartNew();
            var report = await Task.Run(() =>
            {
                // 设备变化时重建引擎
                if (_engine is null || _engineDevice != device)
                {
                    _engine?.Dispose();
                    _engine = new FaceEngine();
                    _engine.Init(ConsoleRunner.ModelsDir, device);
                    _engineDevice = device;
                }

                using var img1 = Cv2.ImRead(path1);
                using var img2 = Cv2.ImRead(path2);
                var r1 = _engine.Process(img1, withQuality: true);
                var r2 = _engine.Process(img2, withQuality: true);
                var sim = (r1 is not null && r2 is not null) ? FaceEngine.Cosine(r1.Embedding, r2.Embedding) : 0f;
                return (r1, r2, sim, _engine.Device, _engine.FallbackNote);
            });
            sw.Stop();

            var (r1, r2, sim, actualDevice, fallbackNote) = report;
            if (r1 is null || r2 is null)
            {
                BeginInvoke(() =>
                {
                    if (r1 is null) ShowImageWithFace(_pic1, path1, null);
                    else ShowImageWithFace(_pic1, path1, r1);
                    if (r2 is null) ShowImageWithFace(_pic2, path2, null);
                    else ShowImageWithFace(_pic2, path2, r2);
                    _result.AppendText(r1 is null ? "图片1 未检测到人脸" : "图片2 未检测到人脸");
                    _status.Text = "未检测到人脸";
                });
                return;
            }
            BeginInvoke(() =>
            {
                ShowImageWithFace(_pic1, path1, r1);
                ShowImageWithFace(_pic2, path2, r2);

                var pass = sim >= threshold / 100f;
                _result.AppendText($"推理设备：{actualDevice}，总耗时 {sw.ElapsedMilliseconds}ms\r\n");
                if (fallbackNote is not null) _result.AppendText(fallbackNote + "\r\n");
                _result.AppendText($"图片1：检测到人脸 {r1.Box}，置信度 {r1.Confidence:F3}{Quality(r1)}\r\n");
                _result.AppendText($"图片2：检测到人脸 {r2.Box}，置信度 {r2.Confidence:F3}{Quality(r2)}\r\n");
                _result.AppendText($"相似度：{sim:F4}（{sim:P1}）\r\n");
                _result.AppendText($"结论：{(pass ? "同一人（≥阈值 " : "非同一人（<阈值 ")}{threshold:F0}%）");
                _result.SelectionStart = Math.Max(0, _result.Text.Length - 40);
                _result.SelectionColor = pass ? Color.Green : Color.Red;
                _status.Text = $"相似度 {sim:P1} · {(pass ? "通过" : "未通过")}";
            });
        }
        catch (Exception ex)
        {
            BeginInvoke(() =>
            {
                _status.Text = "比对失败";
                _result.AppendText("比对失败：" + ex.Message + "\r\n\r\n" + ex.StackTrace);
            });
        }
        finally
        {
            _busy = false;
            BeginInvoke(() => { _compareBtn.Enabled = true; _deviceCombo.Enabled = true; });
        }
    }

    private static string Quality(FaceResult r)
        => r.Quality is null ? "" : $"，yaw/pitch/roll = {r.Quality[0]:F2}/{r.Quality[1]:F2}/{r.Quality[2]:F2}";
}
