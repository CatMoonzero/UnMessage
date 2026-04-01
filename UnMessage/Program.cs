namespace UnMessage
{
    /// <summary>
    /// 应用程序入口类。
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main()
        {
            // 初始化应用程序配置（高 DPI、默认字体等）
            ApplicationConfiguration.Initialize();
            Application.SetDefaultFont(new Font("微软雅黑", 9F));

            Application.ThreadException += (_, e) =>
                MessageBox.Show($"发生未处理异常：{e.Exception.Message}", "客户端错误", MessageBoxButtons.OK, MessageBoxIcon.Error);

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    MessageBox.Show($"发生未处理异常：{ex.Message}", "客户端错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            // 启动主窗体
            Application.Run(new ChatForm());
        }
    }
}