using System;
using System.Windows;

namespace Updater.GUI
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                var app = new Application();
                app.Run(new MainWindow(args));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"程序启动失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(1);
            }
        }
    }
}