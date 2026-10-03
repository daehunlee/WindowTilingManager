using System.Windows;
using System.Windows.Threading;

namespace WindowTilingManager
{
    public partial class App : Application
    {
        public App()
        {
            // 예외로 앱이 죽으면 임베딩된 다른 프로그램 창까지 함께 파괴되므로,
            // UI 스레드 예외는 알리고 계속 실행합니다.
            DispatcherUnhandledException += OnDispatcherUnhandledException;
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                Loc.T("App.UnexpectedError", e.Exception.Message),
                "Window Tiling Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
