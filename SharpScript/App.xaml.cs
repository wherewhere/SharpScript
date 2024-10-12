using SharpScript.Common;
using SharpScript.Helpers;
using SharpScript.Pages;
using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.Core;
using Windows.System.Profile;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SharpScript
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();
            Suspending += OnSuspending;
            UnhandledException += Application_UnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            if (AnalyticsInfo.VersionInfo.DeviceFamily == "Windows.Xbox") { FocusVisualKind = FocusVisualKind.Reveal; }
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used such as when the application is launched to open a specific file.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            EnsureWindow(args);
        }

        private void EnsureWindow(LaunchActivatedEventArgs e)
        {
            if (!isLoaded)
            {
                RegisterExceptionHandlingSynchronizationContext();
                isLoaded = true;
            }

            if (Window.Current is not Window window) { return; }
            WindowHelper.TrackWindow(window);

            // 不要在窗口已包含内容时重复应用程序初始化，
            // 只需确保窗口处于活动状态
            if (window.Content is not Frame rootFrame)
            {
                CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar = true;

                // 创建要充当导航上下文的框架，并导航到第一页
                rootFrame = new Frame();

                rootFrame.NavigationFailed += OnNavigationFailed;

                if (e.PreviousExecutionState == ApplicationExecutionState.Terminated)
                {
                    //TODO: 从之前挂起的应用程序加载状态
                }

                // 将框架放在当前窗口中
                window.Content = rootFrame;

                ThemeHelper.Initialize();
            }

            if (e is LaunchActivatedEventArgs args)
            {
                if (!args.PrelaunchActivated)
                {
                    CoreApplication.EnablePrelaunch(true);
                }
                else { return; }
            }

            if (rootFrame.Content == null)
            {
                // 当导航堆栈尚未还原时，导航到第一页，
                // 并通过将所需信息作为导航参数传入来配置
                // 参数
                rootFrame.Navigate(typeof(MainPage), e, new DrillInNavigationTransitionInfo());
            }

            // 确保当前窗口处于活动状态
            window.Activate();
        }

        /// <summary>
        /// 导航到特定页失败时调用
        /// </summary>
        ///<param name="sender">导航失败的框架</param>
        ///<param name="e">有关导航失败的详细信息</param>
        private static void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        /// <summary>
        /// 在将要挂起应用程序执行时调用。  在不知道应用程序
        /// 无需知道应用程序会被终止还是会恢复，
        /// 并让内存内容保持不变。
        /// </summary>
        /// <param name="sender">挂起的请求的源。</param>
        /// <param name="e">有关挂起请求的详细信息。</param>
        private static void OnSuspending(object sender, SuspendingEventArgs e)
        {
            SuspendingDeferral deferral = e.SuspendingOperation.GetDeferral();
            //TODO: 保存应用程序状态并停止任何后台活动
            deferral.Complete();
        }

        private static void Application_UnhandledException(object sender, Windows.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            SettingsHelper.LogManager?.GetLogger("Unhandled Exception - Application").Error(e.Exception.ExceptionToMessage(), e.Exception);
            e.Handled = true;
        }

        private static void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception Exception)
            {
                SettingsHelper.LogManager?.GetLogger("Unhandled Exception - CurrentDomain").Error(Exception.ExceptionToMessage(), Exception);
            }
        }

        /// <summary>
        /// Should be called from OnActivated and OnLaunched
        /// </summary>
        private static void RegisterExceptionHandlingSynchronizationContext()
        {
            ExceptionHandlingSynchronizationContext
                .Register()
                .UnhandledException += SynchronizationContext_UnhandledException;
        }

        private static void SynchronizationContext_UnhandledException(object sender, Common.UnhandledExceptionEventArgs e)
        {
            SettingsHelper.LogManager?.GetLogger("Unhandled Exception - SynchronizationContext").Error(e.Exception.ExceptionToMessage(), e.Exception);
            e.Handled = true;
        }

        private bool isLoaded;
    }
}
