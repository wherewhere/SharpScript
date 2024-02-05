﻿using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading;

namespace SharpScript.Common
{
    /// <summary>
    /// Wrapper around a standard synchronization context, that catches any unhandled exceptions.
    /// Acts as a façade passing calls to the original SynchronizationContext
    /// </summary>
    /// <example>
    /// Set this up inside your App.xaml.cs file as follows:
    /// <code>
    /// protected override void OnActivated(IActivatedEventArgs args)
    /// {
    ///     EnsureSyncContext();
    ///     ...
    /// }
    /// 
    /// protected override void OnLaunched(LaunchActivatedEventArgs args)
    /// {
    ///     EnsureSyncContext();
    ///     ...
    /// }
    /// 
    /// private void EnsureSyncContext()
    /// {
    ///     var exceptionHandlingSynchronizationContext = ExceptionHandlingSynchronizationContext.Register();
    ///     exceptionHandlingSynchronizationContext.UnhandledException += OnSynchronizationContextUnhandledException;
    /// }
    /// 
    /// private void OnSynchronizationContextUnhandledException(object sender, UnhandledExceptionEventArgs args)
    /// {
    ///     args.Handled = true;
    /// }
    /// </code>
    /// </example>
    public class ExceptionHandlingSynchronizationContext(SynchronizationContext syncContext) : SynchronizationContext
    {
        /// <summary>
        /// Registration method.  Call this from OnLaunched and OnActivated inside the App.xaml.cs
        /// </summary>
        /// <returns></returns>
        public static ExceptionHandlingSynchronizationContext Register()
        {
            SynchronizationContext syncContext = Current ?? throw new InvalidOperationException("Ensure a synchronization context exists before calling this method.");

            if (syncContext is not ExceptionHandlingSynchronizationContext customSynchronizationContext)
            {
                customSynchronizationContext = new ExceptionHandlingSynchronizationContext(syncContext);
                SetSynchronizationContext(customSynchronizationContext);
            }

            return customSynchronizationContext;
        }

        /// <summary>
        /// Links the synchronization context to the specified frame
        /// and ensures that it is still in use after each navigation event
        /// </summary>
        /// <param name="rootFrame"></param>
        /// <returns></returns>
        public static ExceptionHandlingSynchronizationContext RegisterForFrame(Frame rootFrame)
        {
            ArgumentNullException.ThrowIfNull(rootFrame);

            ExceptionHandlingSynchronizationContext synchronizationContext = Register();

            rootFrame.Navigating += (sender, args) => EnsureContext(synchronizationContext);
            rootFrame.Loaded += (sender, args) => EnsureContext(synchronizationContext);

            return synchronizationContext;
        }

        private static void EnsureContext(SynchronizationContext context)
        {
            if (Current != context) { SetSynchronizationContext(context); }
        }

        public override SynchronizationContext CreateCopy() => new ExceptionHandlingSynchronizationContext(syncContext.CreateCopy());


        public override void OperationCompleted() => syncContext.OperationCompleted();


        public override void OperationStarted() => syncContext.OperationStarted();


        public override void Post(SendOrPostCallback d, object state) => syncContext.Post(WrapCallback(d), state);


        public override void Send(SendOrPostCallback d, object state) => syncContext.Send(d, state);


        private SendOrPostCallback WrapCallback(SendOrPostCallback sendOrPostCallback) =>
            state =>
            {
                try
                {
                    sendOrPostCallback(state);
                }
                catch (Exception ex)
                {
                    if (!HandleException(ex)) { throw; }
                }
            };

        private bool HandleException(Exception exception)
        {
            if (UnhandledException == null) { return false; }

            UnhandledExceptionEventArgs exWrapper = new()
            {
                Exception = exception
            };

            UnhandledException(this, exWrapper);

#if DEBUG && !DISABLE_XAML_GENERATED_BREAK_ON_UNHANDLED_EXCEPTION
            if (System.Diagnostics.Debugger.IsAttached) { System.Diagnostics.Debugger.Break(); }
#endif

            return exWrapper.Handled;
        }


        /// <summary>
        /// Listen to this event to catch any unhandled exceptions and allow for handling them
        /// so they don't crash your application
        /// </summary>
        public event EventHandler<UnhandledExceptionEventArgs> UnhandledException;
    }

    public class UnhandledExceptionEventArgs : EventArgs
    {
        public bool Handled { get; set; }
        public Exception Exception { get; init; }
    }
}
