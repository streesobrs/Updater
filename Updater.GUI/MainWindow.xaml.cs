using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Updater.GUI
{
    public partial class MainWindow : Window
    {
        private readonly string[] _args;
        private bool _isUpdating = false;
        private bool _isCancelled = false;
        private bool _isTestMode = false;
        private string _testUpdateType = "zip";
        private int? _performanceTestScale = null;
        private string _testErrorScenario = null;
        private int _logCount = 0;

        public MainWindow(string[] args)
        {
            InitializeComponent();
            _args = args ?? Array.Empty<string>();
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 优先检查帮助命令
            if (_args.Length > 0 && (_args[0].Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                                   _args[0].Equals("-h", StringComparison.OrdinalIgnoreCase)))
            {
                ShowHelp();
                LogPanelBorder.Visibility = Visibility.Visible;
                ConfigPanel.Visibility = Visibility.Collapsed;
                CloseButton.IsEnabled = true;
                return;
            }

            // 检查是否是测试模式
            if (_args.Length > 0 && _args[0].Equals("--test", StringComparison.OrdinalIgnoreCase))
            {
                ParseTestModeArgs();
                _isTestMode = true;
                LogPanelBorder.Visibility = Visibility.Visible;
                ConfigPanel.Visibility = Visibility.Collapsed;
                CancelButton.IsEnabled = true;
                await RunTest();
                return;
            }

            // 普通更新模式：参数充足直接执行，否则显示配置面板
            if (_args.Length >= 5)
            {
                // 命令行有完整参数：自动填入表单并立即执行
                PrefillConfigFromArgs();
                LogPanelBorder.Visibility = Visibility.Visible;
                ConfigPanel.Visibility = Visibility.Collapsed;
                CancelButton.IsEnabled = true;
                await RunUpdateWithArgs(
                    _args[0].Trim('"'),
                    _args[1],
                    Path.GetFullPath(_args[2].Trim('"')),
                    bool.TryParse(_args[3], out bool del) ? del : true,
                    _args[4].ToLower()
                );
            }
            else
            {
                // 没有或参数不足：显示配置面板，用户手动填写
                ShowConfigPanel();
                CloseButton.IsEnabled = true;
                StatusText.Text = "等待配置更新参数";
                UpdateProgress.IsIndeterminate = false;
                UpdateProgress.Value = 0;
            }
        }

        #region 配置面板

        private void ShowConfigPanel()
        {
            ConfigPanel.Visibility = Visibility.Visible;
            LogPanelBorder.Visibility = Visibility.Collapsed;
        }

        private void PrefillConfigFromArgs()
        {
            if (_args.Length >= 1) MainAppExeTextBox.Text = _args[0].Trim('"');
            if (_args.Length >= 2) PackagePathTextBox.Text = _args[1];
            if (_args.Length >= 3) TargetDirTextBox.Text = Path.GetFullPath(_args[2].Trim('"'));
            if (_args.Length >= 4 && bool.TryParse(_args[3], out bool del)) DeletePackageCheckBox.IsChecked = del;
            if (_args.Length >= 5)
            {
                string type = _args[4].ToLower();
                foreach (ComboBoxItem item in UpdateTypeComboBox.Items)
                {
                    if (item.Tag?.ToString() == type)
                    {
                        UpdateTypeComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        // 浏览：主程序文件
        private void BrowseMainAppButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择主程序可执行文件",
                Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog(this) == true)
            {
                MainAppExeTextBox.Text = Path.GetFileName(dlg.FileName);
                // 辅助：如果目标目录还没填，用该文件所在目录填充
                if (string.IsNullOrWhiteSpace(TargetDirTextBox.Text))
                    TargetDirTextBox.Text = Path.GetDirectoryName(dlg.FileName) ?? string.Empty;
            }
        }

        // 浏览：安装包
        private void BrowsePackageButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择更新安装包",
                Filter = "支持的更新包 (*.zip;*.exe)|*.zip;*.exe|ZIP 压缩包 (*.zip)|*.zip|安装程序 (*.exe)|*.exe|所有文件 (*.*)|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog(this) == true)
            {
                PackagePathTextBox.Text = dlg.FileName;
                // 辅助：如果目标目录还没填，用安装包所在目录的上级（例如 update\ 同级）作为建议
                if (string.IsNullOrWhiteSpace(TargetDirTextBox.Text))
                {
                    string? pkgDir = Path.GetDirectoryName(dlg.FileName);
                    if (!string.IsNullOrEmpty(pkgDir))
                        TargetDirTextBox.Text = Directory.GetParent(pkgDir)?.FullName ?? pkgDir;
                }
                // 辅助：根据扩展名自动选择更新类型
                string ext = Path.GetExtension(dlg.FileName).ToLower();
                if (ext == ".zip" && UpdateTypeComboBox.SelectedItem is ComboBoxItem cur && (cur.Tag?.ToString() == "installer"))
                {
                    // 用户之前选了 installer，但现在选 zip → 自动换成 zip 或保持用户选择
                    // 这里不强制替换，尊重用户选择
                }
            }
        }

        // 浏览：目标目录（使用 OpenFileDialog 选目录中任意文件取路径，兼容 net6.0+）
        private void BrowseTargetDirButton_Click(object sender, RoutedEventArgs e)
        {
            // 优先使用 WPF 内置的 OpenFolderDialog（.NET 8+ 可用）
            // 注意：OpenFolderDialog 在 net6.0-windows 中也可在 Microsoft.Win32 下通过 NuGet 获取，
            // 但为兼容 net6.0 使用 OpenFileDialog 的折中方式：提示用户选择目录下任一文件取其目录作为目标。
            try
            {
                // 使用 FolderBrowserDialog 需要引用 System.Windows.Forms，避免增加依赖，这里用更简单的方法：
                // 让用户选择目录中的一个文件（或者我们直接让 explorer 打开一个选择目录的对话框）
                var dlg = new OpenFileDialog
                {
                    Title = "定位目标目录：请选择该目录下的任意一个文件（例如 Software.exe）",
                    Filter = "所有文件 (*.*)|*.*",
                    CheckFileExists = true,
                    Multiselect = false
                };
                if (dlg.ShowDialog(this) == true)
                {
                    string? dir = Path.GetDirectoryName(dlg.FileName);
                    if (!string.IsNullOrEmpty(dir))
                        TargetDirTextBox.Text = dir;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"浏览目录失败：{ex.Message}", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void StartUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            // 1. 收集参数
            string mainAppExe = MainAppExeTextBox.Text?.Trim() ?? string.Empty;
            string packagePath = PackagePathTextBox.Text?.Trim() ?? string.Empty;
            string targetDir = TargetDirTextBox.Text?.Trim() ?? string.Empty;
            bool deleteAfterUpdate = DeletePackageCheckBox.IsChecked == true;

            var selectedType = UpdateTypeComboBox.SelectedItem as ComboBoxItem;
            string updateType = (selectedType?.Tag?.ToString())?.ToLower() ?? "incremental";

            // 2. 验证参数
            if (string.IsNullOrWhiteSpace(mainAppExe))
            {
                MessageBox.Show(this, "请填写主程序文件名（如：Software.exe）", "缺少参数", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            {
                MessageBox.Show(this, "安装包路径为空或文件不存在，请选择有效的更新包", "缺少参数", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(targetDir))
            {
                MessageBox.Show(this, "请填写目标目录", "缺少参数", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // 尝试把目标目录转为绝对路径
            try { targetDir = Path.GetFullPath(targetDir); }
            catch
            {
                MessageBox.Show(this, "目标目录格式无效", "参数错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 3. 隐藏配置面板，显示日志面板并开始更新
            ConfigPanel.Visibility = Visibility.Collapsed;
            LogPanelBorder.Visibility = Visibility.Visible;
            StartUpdateButton.IsEnabled = false;
            CancelButton.IsEnabled = true;

            await RunUpdateWithArgs(mainAppExe, packagePath, targetDir, deleteAfterUpdate, updateType);
        }

        #endregion

        private void ParseTestModeArgs()
        {
            int argIndex = 1;

            // 检查是否有异常测试参数
            if (argIndex < _args.Length && _args[argIndex].Equals("--test-error", StringComparison.OrdinalIgnoreCase))
            {
                argIndex++;
                if (argIndex < _args.Length)
                {
                    _testErrorScenario = _args[argIndex].ToLower();
                    argIndex++;
                }
            }

            // 解析测试类型（默认zip，支持incremental）
            if (argIndex < _args.Length && (_args[argIndex].Equals("zip", StringComparison.OrdinalIgnoreCase) ||
                                           _args[argIndex].Equals("incremental", StringComparison.OrdinalIgnoreCase)))
            {
                _testUpdateType = _args[argIndex].ToLower();
                argIndex++;
                // 解析性能测试级别
                if (argIndex < _args.Length && int.TryParse(_args[argIndex], out int scale))
                {
                    _performanceTestScale = Math.Clamp(scale, 1, 5);
                }
            }
            else
            {
                // 兼容原有逻辑（仅指定级别，默认测试类型为zip）
                if (argIndex < _args.Length && int.TryParse(_args[argIndex], out int scale))
                {
                    _performanceTestScale = Math.Clamp(scale, 1, 5);
                    argIndex++;
                }
            }
        }

        private void ShowHelp()
        {
            string helpText = @"
======================================= Updater.GUI 帮助文档 =======================================
程序功能：支持ZIP全量更新、安装包更新、增量包更新，以及测试模式

【1. 普通更新模式（命令行参数自动执行）】
参数格式：
  Updater.GUI.exe <主程序文件名> <安装包路径> <目标目录> <是否删除安装包> <更新类型>

【2. 图形配置模式（双击直接运行）】
  直接启动 Updater.GUI.exe，在界面中填写参数后点击「开始更新」

【3. 测试模式】
参数格式：
  Updater.GUI.exe --test [--test-error 异常类型] [测试类型] [性能测试强度]

【4. 帮助命令】
  Updater.GUI.exe --help
  Updater.GUI.exe -h
";
            Log(helpText);
            StatusText.Text = "帮助信息";
        }

        private async Task RunTest()
        {
            _isUpdating = true;
            try
            {
                Log("===== 测试模式启动 =====");
                Log($"版本号: {Assembly.GetExecutingAssembly().GetName().Version}");
                Log($"测试类型: {_testUpdateType}");
                Log($"性能级别: {(_performanceTestScale.HasValue ? _performanceTestScale.Value.ToString() : "基础测试")}");

                if (_testErrorScenario != null)
                {
                    Log($"异常场景: {_testErrorScenario}");
                    StatusText.Text = $"测试模式 - 异常测试: {_testErrorScenario}";
                }
                else if (_performanceTestScale.HasValue)
                {
                    StatusText.Text = $"测试模式 - 性能测试 (强度{_performanceTestScale.Value})";
                }
                else
                {
                    StatusText.Text = "测试模式 - 基础功能测试";
                }

                // 单文件发布兼容性：使用 AppContext.BaseDirectory
                string consoleExePathForTest = Path.Combine(AppContext.BaseDirectory, "Updater.exe");

                if (!File.Exists(consoleExePathForTest))
                {
                    Log($"错误：控制台版本不存在 - {consoleExePathForTest}");
                    Log("提示：Updater.exe（控制台版本）需与 Updater.GUI.exe 放在同一目录下");
                    StatusText.Text = "控制台版本未找到";
                    CloseButton.IsEnabled = true;
                    return;
                }

                // 构建命令行参数
                string arguments = "--test";
                if (_testErrorScenario != null)
                    arguments += $" --test-error {_testErrorScenario}";
                arguments += $" {_testUpdateType}";
                if (_performanceTestScale.HasValue)
                    arguments += $" {_performanceTestScale.Value}";

                Log($"启动控制台测试: {consoleExePathForTest} {arguments}");

                using (var process = new Process())
                {
                    process.StartInfo.FileName = consoleExePathForTest;
                    process.StartInfo.Arguments = arguments;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;

                    process.OutputDataReceived += (_, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data)) Log(e.Data);
                    };
                    process.ErrorDataReceived += (_, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data)) Log($"错误: {e.Data}");
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    StatusText.Text = "正在执行测试...";
                    UpdateProgress.IsIndeterminate = true;

                    await Task.Run(() => process.WaitForExit());
                    Log($"测试完成，退出码: {process.ExitCode}");
                }

                UpdateProgress.IsIndeterminate = false;
                UpdateProgress.Value = 100;
                ProgressText.Text = "100%";
                StatusText.Text = "测试完成";
                Log("===== 测试结束 =====");
                CloseButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Log($"测试过程发生错误: {ex.Message}");
                StatusText.Text = "测试失败";
            }
            finally
            {
                _isUpdating = false;
                CancelButton.IsEnabled = false;
                CloseButton.IsEnabled = true;
            }
        }

        private async Task RunUpdateWithArgs(string mainAppExe, string packagePath, string targetDir, bool deleteAfterUpdate, string updateType)
        {
            _isUpdating = true;
            try
            {
                Log("===== 更新程序启动 =====");
                Log($"版本号: {Assembly.GetExecutingAssembly().GetName().Version}");
                Log($"主程序: {mainAppExe}");
                Log($"安装包: {packagePath}");
                Log($"目标目录: {targetDir}");
                Log($"删除安装包: {deleteAfterUpdate}");
                Log($"更新类型: {updateType}");

                // 验证安装包
                if (!File.Exists(packagePath))
                {
                    Log($"错误：安装包不存在 - {packagePath}");
                    StatusText.Text = "安装包不存在";
                    UpdateProgress.IsIndeterminate = false;
                    UpdateProgress.Value = 0;
                    CancelButton.IsEnabled = false;
                    CloseButton.IsEnabled = true;
                    return;
                }

                // 调用控制台版本执行真正的更新逻辑（单文件发布兼容：AppContext.BaseDirectory）
                string consoleExePath = Path.Combine(AppContext.BaseDirectory, "Updater.exe");

                if (!File.Exists(consoleExePath))
                {
                    Log($"错误：控制台版本不存在 - {consoleExePath}");
                    Log("提示：Updater.exe（控制台版本）需与 Updater.GUI.exe 放在同一目录下");
                    StatusText.Text = "控制台版本未找到";
                    CloseButton.IsEnabled = true;
                    return;
                }

                // 构建命令行参数（路径含空格，加引号）
                string arguments = $"\"{mainAppExe}\" \"{packagePath}\" \"{targetDir}\" {deleteAfterUpdate.ToString().ToLower()} {updateType}";
                Log($"启动控制台更新程序: {consoleExePath}");
                Log($"参数: {arguments}");

                StatusText.Text = "正在准备更新...";
                Log("开始执行更新操作...");
                UpdateProgress.IsIndeterminate = true;

                int consoleExitCode = -1;

                using (var process = new Process())
                {
                    process.StartInfo.FileName = consoleExePath;
                    process.StartInfo.Arguments = arguments;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;

                    process.OutputDataReceived += (_, e) =>
                    {
                        if (string.IsNullOrEmpty(e.Data)) return;
                        Dispatcher.Invoke(() =>
                        {
                            Log(e.Data);
                            // 尝试解析日志中的百分比更新进度条
                            var match = Regex.Match(e.Data, @"(\d+(?:\.\d+)?)\s*%");
                            if (match.Success && double.TryParse(match.Groups[1].Value, out double percent))
                            {
                                UpdateProgress.IsIndeterminate = false;
                                UpdateProgress.Value = Math.Clamp(percent, 0, 100);
                                ProgressText.Text = $"{percent:F0}%";
                            }
                        });
                    };
                    process.ErrorDataReceived += (_, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data)) Log($"错误: {e.Data}");
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    // 支持取消：每 100ms 轮询取消标志
                    var waitTask = Task.Run(() => process.WaitForExit());
                    while (!waitTask.IsCompleted)
                    {
                        if (_isCancelled)
                        {
                            try
                            {
                                process.Kill(entireProcessTree: true);
                                Log("用户取消，已终止更新进程");
                            }
                            catch { }
                            break;
                        }
                        await Task.Delay(100);
                    }
                    await waitTask;
                    consoleExitCode = process.ExitCode;
                }

                if (_isCancelled)
                {
                    Log("更新已取消");
                    StatusText.Text = "更新已取消";
                    UpdateProgress.IsIndeterminate = false;
                    UpdateProgress.Value = 0;
                    CloseButton.IsEnabled = true;
                    return;
                }

                bool updateSuccess = (consoleExitCode == 0);

                if (updateSuccess)
                {
                    // 删除安装包、启动主程序由控制台版本负责
                    UpdateProgress.IsIndeterminate = false;
                    UpdateProgress.Value = 100;
                    ProgressText.Text = "100%";
                    StatusText.Text = "更新完成";
                    Log("===== 更新完成 =====");
                }
                else
                {
                    Log($"更新失败，控制台进程退出码: {consoleExitCode}");
                    StatusText.Text = "更新失败";
                    UpdateProgress.IsIndeterminate = false;
                    UpdateProgress.Value = 0;
                }

                CloseButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Log($"更新过程发生错误: {ex.Message}");
                StatusText.Text = "更新失败";
                UpdateProgress.IsIndeterminate = false;
                UpdateProgress.Value = 0;
                CloseButton.IsEnabled = true;
            }
            finally
            {
                _isUpdating = false;
                CancelButton.IsEnabled = false;
                CloseButton.IsEnabled = true;
                StartUpdateButton.IsEnabled = true;
            }
        }

        private void Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                _logCount++;
                LogTextBox.AppendText($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - {message}{Environment.NewLine}");
                LogTextBox.ScrollToEnd();
                LogCountText.Text = $"{_logCount} 条";
            });
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isUpdating)
            {
                _isCancelled = true;
                StatusText.Text = "正在取消...";
                CancelButton.IsEnabled = false;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
